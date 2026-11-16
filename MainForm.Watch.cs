using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace ZapretControlApp;

public partial class MainForm
{
    BypassState state = BypassState.Off;
    string stateDetail = "";
    DateTime lastCheckAt = DateTime.MinValue, lastOwnRestart = DateTime.MinValue, hourStart = DateTime.Now;
    bool lastCheckOk, restarting, autoRunning, updating, diagRunning, checkBusy, pendingWake;
    string lastStartError, pendingReason, lastNetSig;
    int problemRestarts, okChecks, badChecks;
    readonly List<DateTime> crashTimes = new List<DateTime>();
    readonly HashSet<string> skipLogged = new HashSet<string>();
    readonly List<string> winwsTail = new List<string>();
    System.Windows.Forms.Timer healthTimer, debounceTimer, firstCheckTimer;

    Check chkWatch, chkWake, chkCrash;
    Dropdown cbInterval, cbReaction;
    RoundButton btnCheckNow;
    Label lblWatchInfo;

    bool IsBusy => autoRunning || diagRunning;
    bool WatchActive => cfg.WatchEnabled && runner.IsRunning && !shuttingDown;

    static string StateName(BypassState s) => s switch
    {
        BypassState.Working => "работает",
        BypassState.Problems => "проблемы",
        BypassState.NoNetwork => "нет сети",
        BypassState.Crashed => "сбой",
        _ => "выключен",
    };

    void RememberOutput(string s)
    {
        if (s == null || !s.StartsWith("winws:")) return;
        lock (winwsTail)
        {
            winwsTail.Add(s);
            if (winwsTail.Count > 80) winwsTail.RemoveAt(0);
        }
    }

    string WinwsTailText()
    {
        lock (winwsTail) return string.Join("\n", winwsTail);
    }

    void InitWatch()
    {
        healthTimer = new System.Windows.Forms.Timer();
        healthTimer.Tick += (s, e) => { if (WatchActive) _ = RunHealthCheckAsync(false); };
        debounceTimer = new System.Windows.Forms.Timer { Interval = 7000 };
        debounceTimer.Tick += (s, e) => { debounceTimer.Stop(); _ = OnDebouncedAsync(); };
        firstCheckTimer = new System.Windows.Forms.Timer { Interval = 10000 };
        firstCheckTimer.Tick += (s, e) => { firstCheckTimer.Stop(); if (WatchActive) _ = RunHealthCheckAsync(false); };
        ApplyWatchSettings();

        _ = Task.Run(() => lastNetSig = NetInfo.Signature());
        SystemEvents.PowerModeChanged += (s, e) =>
        {
            if (e.Mode == PowerModes.Resume) SafeInvoke(() => QueueRestart("после выхода из сна", true));
        };
        NetworkChange.NetworkAddressChanged += (s, e) => SafeInvoke(() => QueueRestart("после смены сети", false));
    }

    void StopWatchTimers()
    {
        healthTimer?.Stop();
        debounceTimer?.Stop();
        firstCheckTimer?.Stop();
    }

    void ApplyWatchSettings()
    {
        if (healthTimer == null) return;
        healthTimer.Interval = Math.Max(1, cfg.WatchIntervalMin) * 60000;
        healthTimer.Enabled = cfg.WatchEnabled && cfg.WatchIntervalMin > 0;
    }

    void ScheduleFirstCheck()
    {
        if (!cfg.WatchEnabled || firstCheckTimer == null) return;
        firstCheckTimer.Stop();
        firstCheckTimer.Start();
    }

    void OnBypassStarted()
    {
        crashTimes.Clear();
        problemRestarts = 0;
        lastCheckAt = DateTime.MinValue;
        skipLogged.Clear();
        SetState(BypassState.Working);
        ScheduleFirstCheck();
    }

    void OnBypassStopped()
    {
        firstCheckTimer?.Stop();
        problemRestarts = 0;
        lastCheckAt = DateTime.MinValue;
        SetState(BypassState.Off);
    }

    void SyncState()
    {
        if (restarting) return;
        if (runner.IsRunning) { if (state == BypassState.Off || state == BypassState.Crashed) state = BypassState.Working; }
        else if (state != BypassState.Crashed && state != BypassState.Off) state = BypassState.Off;
    }

    void SetState(BypassState s, string detail = "")
    {
        var old = state;
        string d = detail ?? "";
        state = s;
        stateDetail = d;
        if (old != s)
        {
            AddLog("Состояние: " + StateName(s) + (d.Length > 0 ? " — " + d : "") + ".");
            if (s == BypassState.Problems)
                Notify("problems", true, "Zapret: проблемы", d, ToolTipIcon.Warning, () => ShowTab(PgLog));
            else if (s == BypassState.Working && old == BypassState.Problems)
                Notify("recovered", false, "Zapret восстановился", "Сервисы снова открываются.", ToolTipIcon.Info, () => ShowTab(PgLog));
        }
        UpdateUi();
    }

    void QueueRestart(string reason, bool wake)
    {
        if (!cfg.WatchEnabled || !cfg.RestartOnWake || !runner.IsRunning || restarting || shuttingDown) return;
        if (!wake && (DateTime.Now - lastOwnRestart).TotalSeconds < 20) return;
        if (wake || pendingReason == null) pendingReason = reason;
        if (wake) pendingWake = true;
        debounceTimer.Stop();
        debounceTimer.Start();
    }

    async Task OnDebouncedAsync()
    {
        string reason = pendingReason;
        bool wake = pendingWake;
        pendingReason = null;
        pendingWake = false;
        if (!runner.IsRunning || restarting) return;
        string sig = await Task.Run(NetInfo.Signature);
        bool changed = sig != lastNetSig;
        lastNetSig = sig;
        if (!wake && !changed) { AddLog("Сетевое событие без смены адресов — перезапуск не нужен."); return; }
        await RestartAsync("перезапуск " + reason, true);
    }

    /// <summary>Остановка и запуск winws с теми же настройками. Тяжёлые вызовы идут в фоне, окно не зависает.</summary>
    async Task<bool> RestartAsync(string reason, bool setWorking)
    {
        if (restarting || shuttingDown || autoRunning || updating) return false;
        var st = SelectedStrategy();
        if (st == null || !Strategy.IsValidZapretDir(cfg.ZapretPath)) return false;

        restarting = true;
        AddLog("Перезапуск обхода: " + reason + ".");
        string dir = cfg.ZapretPath, game = GameMode(), err = null;
        bool ok = false;
        try
        {
            (ok, err) = await Task.Run(() =>
            {
                runner.Stop(true);
                Thread.Sleep(500);
                bool r = runner.Start(dir, st, game, out string e2);
                return (r, e2);
            });
        }
        catch (Exception ex) { Diag.Write("restart", ex); err = ex.Message; }
        finally { restarting = false; }

        lastOwnRestart = DateTime.Now;
        if (shuttingDown) return ok;
        if (ok)
        {
            lastStartError = null;
            AddLog("Обход перезапущен: " + st.Name + " (PID " + runner.Pid + ").");
            if (setWorking) SetState(BypassState.Working);
            ScheduleFirstCheck();
        }
        else
        {
            lastStartError = err;
            AddLog("Перезапуск не удался: " + err);
            SetState(BypassState.Crashed, "не удалось перезапустить обход");
        }
        UpdateUi();
        return ok;
    }

    void OnRunnerStopped()
    {
        if (shuttingDown || restarting || autoRunning || updating) return;
        AddLog("winws.exe завершился сам (не по команде). Смотрите журнал выше.");

        var now = DateTime.Now;
        crashTimes.RemoveAll(t => now - t > TimeSpan.FromMinutes(10));
        bool auto = cfg.WatchEnabled && cfg.RestartOnCrash;
        if (auto && crashTimes.Count < 3)
        {
            crashTimes.Add(now);
            SetState(BypassState.Crashed, "winws.exe завершился, перезапускаю");
            _ = CrashRestartAsync();
            return;
        }
        if (auto) AddLog("winws.exe упал больше 3 раз за 10 минут — автоперезапуск остановлен.");
        FinishCrash(auto);
    }

    async Task CrashRestartAsync()
    {
        bool ok = await RestartAsync("winws.exe завершился сам", true);
        if (ok) Notify("crash-restarted", true, "Zapret упал и перезапущен", "Обход снова работает.", ToolTipIcon.Warning, () => ShowTab(PgLog));
        else FinishCrash(true);
    }

    void FinishCrash(bool auto)
    {
        _ = Task.Run(Runner.CleanupDriver);
        RestoreTimestamps();
        SetState(BypassState.Crashed, auto ? "winws.exe упал и больше не перезапускается" : "winws.exe завершился сам");
        Notify("crash-final", true, "Zapret упал и больше не перезапускается",
            auto ? "Слишком частые сбои, автоперезапуск остановлен. Подробности — в журнале." : "Обход остановлен. Подробности — в журнале.",
            ToolTipIcon.Error, () => ShowTab(PgLog));
    }

    void CheckNow()
    {
        if (!runner.IsRunning) { AddLog("Проверка: обход выключен."); return; }
        _ = RunHealthCheckAsync(true);
    }

    void SkipOnce(string why)
    {
        if (skipLogged.Add(why)) AddLog("Периодическая проверка приостановлена: " + why + " (результат был бы бессмысленным).");
    }

    /// <summary>2–4 домена: по одному на выбранный сервис (без Telegram) и свои домены.</summary>
    List<Target> HealthTargets()
    {
        var list = new List<Target>();
        foreach (var svc in TestTargets.Services)
        {
            if (svc.Short == "TG" || !cfg.TestServices.Contains(svc.Name)) continue;
            list.Add(new Target { Domain = svc.Domains[0], Group = svc.Short });
        }
        foreach (var d in cfg.CustomDomains)
        {
            if (list.Count >= 4) break;
            if (!list.Any(x => x.Domain == d)) list.Add(new Target { Domain = d });
        }
        return list;
    }

    static string FailText(List<(Target Target, bool Ok)> failed)
    {
        var names = new List<string>();
        foreach (var f in failed)
        {
            string n = f.Target.Group == null ? f.Target.Domain : TestTargets.Services.First(x => x.Short == f.Target.Group).Name;
            if (!names.Contains(n)) names.Add(n);
        }
        return string.Join(", ", names) + (names.Count == 1 ? " не отвечает" : " не отвечают");
    }

    void HourlySummary()
    {
        if ((DateTime.Now - hourStart).TotalHours < 1) return;
        if (okChecks + badChecks > 0) AddLog("Проверки за последний час: успешных " + okChecks + ", с проблемами " + badChecks + ".");
        okChecks = badChecks = 0;
        hourStart = DateTime.Now;
    }

    async Task RunHealthCheckAsync(bool manual)
    {
        if (checkBusy || shuttingDown || restarting) return;
        if (!runner.IsRunning) return;
        string why = autoRunning ? "идёт автоподбор" : updating ? "идёт обновление" : diagRunning ? "идёт диагностика" : null;
        if (why != null) { SkipOnce(why); return; }

        checkBusy = true;
        try
        {
            if (!manual)
            {
                var route = await Task.Run(() => SystemTools.InspectDefaultRoute());
                if (route != null && route.IsVpn) { SkipOnce("интернет идёт через VPN-адаптер «" + route.Name + "» (режим TUN)"); return; }
            }
            skipLogged.Clear();

            var targets = HealthTargets();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var ct = cts.Token;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                var net = await NetInfo.ProbeAsync(ct);
                if (!runner.IsRunning || restarting) return;
                if (!net.Ok)
                {
                    lastCheckAt = DateTime.Now;
                    lastCheckOk = false;
                    SetState(BypassState.NoNetwork, "нет доступа в интернет");
                    return;
                }

                var failed = new List<(Target Target, bool Ok)>();
                if (targets.Count > 0) failed = (await Tester.Check(targets, ct)).Where(x => !x.Ok).ToList();
                if (!runner.IsRunning || restarting) return;

                if (failed.Count == 0)
                {
                    lastCheckAt = DateTime.Now;
                    lastCheckOk = true;
                    okChecks++;
                    problemRestarts = 0;
                    HourlySummary();
                    if (manual) AddLog("Проверка: все выбранные сервисы открываются.");
                    if (state != BypassState.Working) SetState(BypassState.Working); else UpdateUi();
                    return;
                }

                string detail = FailText(failed);
                if (attempt == 1)
                {
                    AddLog("Проверка: " + detail + ". Повторю через 15 с, один сбой не считается.");
                    await Task.Delay(15000, ct);
                    continue;
                }

                lastCheckAt = DateTime.Now;
                lastCheckOk = false;
                badChecks++;
                HourlySummary();
                AddLog("Проверка: " + detail + " (два раза подряд).");
                SetState(BypassState.Problems, detail);

                if (!manual && cfg.OnProblems != "report")
                {
                    int limit = cfg.OnProblems == "ten" ? 10 : 1;
                    if (problemRestarts < limit)
                    {
                        problemRestarts++;
                        await RestartAsync("проблемы (" + detail + "), попытка " + problemRestarts + " из " + limit, false);
                    }
                    else if (problemRestarts == limit)
                    {
                        problemRestarts++;
                        AddLog("Лимит перезапусков при проблемах исчерпан — дальше только сообщаю.");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Diag.Write("health", ex); }
        finally { checkBusy = false; }
    }

    string StatusLine()
    {
        if (!runner.IsRunning)
        {
            if (state == BypassState.Crashed) return "✗ " + (stateDetail.Length > 0 ? stateDetail : "обход остановился из-за сбоя");
            return Strategy.IsValidZapretDir(cfg.ZapretPath)
                ? "Выберите стратегию и нажмите «Включить»"
                : "Сначала укажите папку zapret на вкладке «Настройки»";
        }
        string st = "Стратегия: " + runner.CurrentStrategy;
        switch (state)
        {
            case BypassState.Problems: return "⚠ " + stateDetail + "   ·   " + st;
            case BypassState.NoNetwork: return "⚠ Нет сети, жду подключения   ·   " + st;
            case BypassState.Crashed: return "✗ " + stateDetail;
            default:
                return st + "   ·   " + (lastCheckAt == DateTime.MinValue ? "проверка…" : "проверено " + lastCheckAt.ToString("HH:mm") + (lastCheckOk ? " ✓" : " ⚠"));
        }
    }

    string StateTitle() => state switch
    {
        BypassState.Working => "Zapret включён",
        BypassState.Problems => "Zapret включён · проблемы",
        BypassState.NoNetwork => "Zapret включён · нет сети",
        BypassState.Crashed => "Zapret · сбой",
        _ => "Zapret выключен",
    };

    void RefreshWatchInfo()
    {
        if (lblWatchInfo == null) return;
        lblWatchInfo.Text = !runner.IsRunning ? "Обход выключен."
            : lastCheckAt == DateTime.MinValue ? "Проверок ещё не было."
            : "Последняя проверка: " + lastCheckAt.ToString("HH:mm:ss") + (lastCheckOk ? " ✓" : " ⚠");
    }

    Panel BuildPageWatch()
    {
        var p = Page();
        var host = new ScrollHost { Dock = DockStyle.Fill, BackColor = Bg, Margin = Padding.Empty };
        var c = host.Content;
        c.BackColor = Bg;
        c.SuspendLayout();

        c.Controls.Add(Caption("Периодическая проверка и автовосстановление", Fonts.Heading, TextC, 32));
        var sub = Caption("Пока обход включён, программа редко проверяет, что сервисы открываются, и сама оживляет обход после сна, смены сети или сбоя.", Fonts.Small, Muted, 26);
        sub.Margin = Ui.Pad(0, 0, 0, 10);
        c.Controls.Add(sub);

        chkWatch = Chk("Проверять работу обхода и восстанавливать его");
        chkWatch.Checked = cfg.WatchEnabled;
        chkWatch.CheckedChanged += (s, e) =>
        {
            if (loading) return;
            cfg.WatchEnabled = chkWatch.Checked;
            cfg.Save();
            ApplyWatchSettings();
        };

        chkWake = Chk("Перезапускать обход после сна и смены сети");
        chkWake.Checked = cfg.RestartOnWake;
        chkWake.CheckedChanged += (s, e) => { if (!loading) { cfg.RestartOnWake = chkWake.Checked; cfg.Save(); } };

        chkCrash = Chk("Перезапускать обход после падения winws.exe (не больше 3 раз за 10 минут)");
        chkCrash.Checked = cfg.RestartOnCrash;
        chkCrash.CheckedChanged += (s, e) => { if (!loading) { cfg.RestartOnCrash = chkCrash.Checked; cfg.Save(); } };

        foreach (var chk in new[] { chkWatch, chkWake, chkCrash })
        {
            chk.Margin = new Padding(chk.Margin.Left, 0, 0, Ui.S(8));
            c.Controls.Add(chk);
        }

        cbInterval = Drop(160);
        foreach (var o in new[] { ("Выкл", "0"), ("Каждые 2 минуты", "2"), ("Каждые 5 минут", "5"), ("Каждые 10 минут", "10") })
            cbInterval.Items.Add(new Opt { Text = o.Item1, Value = o.Item2 });
        cbInterval.SelectedIndex = Math.Max(0, cbInterval.Items.Cast<Opt>().ToList().FindIndex(o => o.Value == cfg.WatchIntervalMin.ToString()));
        cbInterval.SelectedIndexChanged += (s, e) =>
        {
            if (loading || !(cbInterval.SelectedItem is Opt o)) return;
            cfg.WatchIntervalMin = int.Parse(o.Value);
            cfg.Save();
            ApplyWatchSettings();
        };
        c.Controls.Add(OptionRow("Интервал проверки:", cbInterval));

        cbReaction = Drop(300);
        foreach (var o in new[] { ("Только сообщить", "report"), ("Перезапустить Zapret 1 раз", "once"), ("Перезапустить Zapret не более 10 раз", "ten") })
            cbReaction.Items.Add(new Opt { Text = o.Item1, Value = o.Item2 });
        cbReaction.SelectedIndex = Math.Max(0, cbReaction.Items.Cast<Opt>().ToList().FindIndex(o => o.Value == cfg.OnProblems));
        cbReaction.SelectedIndexChanged += (s, e) =>
        {
            if (loading || !(cbReaction.SelectedItem is Opt o)) return;
            cfg.OnProblems = o.Value;
            cfg.Save();
        };
        c.Controls.Add(OptionRow("Если есть проблемы:", cbReaction));

        var row = new FlowLayoutPanel { BackColor = Bg, Height = Ui.S(46), WrapContents = false, Margin = Ui.Pad(0, 4, 0, 8) };
        btnCheckNow = Btn("Проверить сейчас", Line, 200, 34);
        btnCheckNow.Margin = Ui.Pad(0, 0, 14, 0);
        btnCheckNow.Click += (s, e) => CheckNow();
        lblWatchInfo = Lbl("", Muted);
        lblWatchInfo.Margin = Ui.Pad(0, 7, 0, 0);
        row.Controls.AddRange(new Control[] { btnCheckNow, lblWatchInfo });
        c.Controls.Add(row);

        c.Controls.Add(new FitNote
        {
            BackColor = Surface, ForeColor = Muted, Height = Ui.S(120), Margin = Ui.Pad(0, 6, 0, 0),
            Text = "Как это работает:\n" +
                   "• Проверка лёгкая: 2–4 сайта выбранных сервисов и ваши домены, Telegram не учитывается.\n" +
                   "• «Проблемы» ставятся после двух неудачных проверок подряд.\n" +
                   "• Нет интернета — состояние «Нет сети», ничего не перезапускается.\n" +
                   "• Проверка не выполняется во время автоподбора, обновления и при VPN в режиме TUN.",
        });

        c.ResumeLayout(true);
        p.Controls.Add(host);
        return p;
    }

    Control OptionRow(string label, Control ctl)
    {
        var row = new FlowLayoutPanel { BackColor = Bg, Height = Ui.S(42), WrapContents = false, Margin = Ui.Pad(0, 0, 0, 4) };
        var l = Lbl(label, Muted);
        l.Margin = Ui.Pad(0, 8, 12, 0);
        ctl.Margin = Ui.Pad(0, 3, 0, 0);
        row.Controls.Add(l);
        row.Controls.Add(ctl);
        return row;
    }
}
