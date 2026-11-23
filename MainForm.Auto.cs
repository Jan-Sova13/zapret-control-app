using System.Text;
using System.Text.RegularExpressions;

namespace ZapretControlApp;

public partial class MainForm
{
    const int DeepRepeats = 3;

    const string AddDomainsHelp =
        "Свои домены для проверки\n\n" +
        "• Добавить в список обхода — записывает домены из поля в «Свой список: домены для обхода». " +
        "Zapret обрабатывает только домены из своих списков: если домена там нет, Zapret на него не действует, и проверка покажет, что он «не открылся» при любой стратегии.\n" +
        "• Взять из моих списков — копирует в поле домены из «Свой список: домены для обхода», чтобы проверять и их.\n\n" +
        "Сервисы выше (YouTube, Discord, Cloudflare) уже есть в стоковых списках, добавлять их не нужно.\n" +
        "Чтобы изменения в списке применились, перезапустите Zapret.";
    Check[] chkServices;
    Check chkStopFirst;
    ScrollText txtCustom;
    TipList lbResults;
    ThinProgress prog;
    Label lblAuto;
    RoundButton btnAuto, btnDeep, btnAutoCancel, btnApplyBest, btnAddToList, btnImportDomains;
    Dropdown cbCand, cbSpeed;
    TableLayoutPanel autoLeft;
    Control autoAdvRow;
    CancellationTokenSource autoCts;
    List<TestResult> results = new List<TestResult>();
    List<TestResult> resultView = new List<TestResult>();

    Panel BuildPageAuto()
    {
        var p = Page();
        var t = Grid(1, 3);
        Cols(t, Fill());
        Rows(t, Abs(90), Fill(), Abs(108));

        var hint = Caption(
            "Для каждой стратегии программа запускает winws, открывает по HTTPS сайты выбранных сервисов и ваши домены и считает, сколько открылось. " +
            "Стратегии проверяются по очереди: одновременно их запускать нельзя, они бы перехватывали трафик друг друга. " +
            "«Глубокий» режим дополнительно качает данные с сервисов и повторяет проверку 3 раза: так видно замедление и нестабильность. " +
            "Видеопоток YouTube (googlevideo) напрямую проверить нельзя — адреса подписаны, поэтому берутся скрипт плеера и картинки. " +
            "VPN на время проверки лучше выключить.",
            Fonts.Small, Muted, 90);
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.TopLeft;
        t.Controls.Add(hint, 0, 0);

        var body = Grid(2, 1);
        Cols(body, (SizeType.Percent, 48), (SizeType.Percent, 52));

        var left = Grid(1, 7);
        autoLeft = left;
        left.Margin = Ui.Pad(0, 0, 16, 0);
        Rows(left, Abs(26), Abs(36), Abs(34), Abs(0), Abs(26), Fill(), Abs(46));
        left.Controls.Add(Lbl("Что проверять:", Muted), 0, 0);

        var sf = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Bg, Margin = Padding.Empty };
        chkServices = new Check[TestTargets.Services.Length];
        for (int i = 0; i < chkServices.Length; i++)
        {
            var c = Chk(TestTargets.Services[i].Name);
            c.Margin = Ui.Pad(0, 0, 14, 6);
            c.Checked = cfg.TestServices.Contains(TestTargets.Services[i].Name);
            chkServices[i] = c;
            sf.Controls.Add(c);
        }
        left.Controls.Add(sf, 0, 1);

        chkStopFirst = Chk("Остановиться, как только всё откроется (только для быстрой проверки)");
        chkStopFirst.Checked = cfg.StopOnFirstSuccess;
        tip.SetToolTip(chkStopFirst, "Только для быстрой проверки. Она закончится на первой стратегии, при которой открылись все выбранные сервисы и домены.\nБыстрее, но полного рейтинга остальных стратегий не будет.");
        left.Controls.Add(chkStopFirst, 0, 2);

        var adv = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Bg, Margin = Padding.Empty, WrapContents = false, Padding = Ui.Pad(0, 4, 0, 0) };
        var l1 = Lbl("Глубокий: кандидатов", Muted);
        l1.Margin = Ui.Pad(0, 6, 8, 0);
        cbCand = Drop(70);
        cbCand.Margin = Ui.Pad(0, 0, 14, 0);
        foreach (var n in new[] { 3, 5, 8, 10 }) cbCand.Items.Add(new Opt { Text = n.ToString(), Value = n.ToString() });
        cbCand.SelectedIndex = Math.Max(0, cbCand.Items.Cast<Opt>().ToList().FindIndex(o => o.Value == cfg.DeepCandidates.ToString()));
        cbCand.SelectedIndexChanged += (s, e) => { if (!loading && cbCand.SelectedItem is Opt o) { cfg.DeepCandidates = int.Parse(o.Value); cfg.Save(); } };
        var l2 = Lbl("порог скорости", Muted);
        l2.Margin = Ui.Pad(0, 6, 8, 0);
        cbSpeed = Drop(130);
        foreach (var n in new[] { 100, 300, 500, 1000 }) cbSpeed.Items.Add(new Opt { Text = n + " КБ/с", Value = n.ToString() });
        cbSpeed.SelectedIndex = Math.Max(0, cbSpeed.Items.Cast<Opt>().ToList().FindIndex(o => o.Value == cfg.SpeedThresholdKB.ToString()));
        cbSpeed.SelectedIndexChanged += (s, e) => { if (!loading && cbSpeed.SelectedItem is Opt o) { cfg.SpeedThresholdKB = int.Parse(o.Value); cfg.Save(); } };
        tip.SetToolTip(cbSpeed, "Ниже этой скорости загрузки сервис в глубоком режиме считается «медленным».");
        adv.Controls.AddRange(new Control[] { l1, cbCand, l2, cbSpeed });
        autoAdvRow = adv;
        left.Controls.Add(adv, 0, 3);

        left.Controls.Add(Lbl("Свои домены для проверки (по одному в строке):", Muted), 0, 4);
        txtCustom = Input(true);
        txtCustom.Dock = DockStyle.Fill;
        txtCustom.Text = string.Join(Environment.NewLine, cfg.CustomDomains);
        left.Controls.Add(txtCustom, 0, 5);

        var lf = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Bg, Padding = Ui.Pad(0, 12, 0, 0), Margin = Padding.Empty };
        btnAddToList = Btn("Добавить в список обхода", Line, 220, 34);
        btnAddToList.Click += (s, e) => AddDomainsToUserList(ParseDomains(txtCustom.Text), false);
        btnImportDomains = Btn("Взять из моих списков", Line, 200, 34);
        btnImportDomains.Margin = Ui.Pad(10, 0, 0, 0);
        btnImportDomains.Click += (s, e) => ImportUserDomains();
        lf.Controls.Add(btnAddToList);
        lf.Controls.Add(btnImportDomains);
        var helpAdd = HelpBtn("Добавить мои домены", AddDomainsHelp);
        helpAdd.Margin = Ui.Pad(10, 0, 0, 0);
        lf.Controls.Add(helpAdd);
        left.Controls.Add(lf, 0, 6);
        body.Controls.Add(left, 0, 0);

        var right = Grid(1, 2);
        Rows(right, Abs(26), Fill());
        right.Controls.Add(Lbl("Результаты (двойной клик — выбрать стратегию):", Muted), 0, 0);
        lbResults = new TipList { Dock = DockStyle.Fill, Font = Fonts.Mono, BackColor = Field, ForeColor = TextC, RowHeight = Ui.S(26), Margin = Padding.Empty };
        lbResults.TipFor = i => i >= 0 && i < resultView.Count ? (resultView[i] is DeepResult d ? d.Tip : RowText(resultView[i])) : "";
        lbResults.ItemActivated += (s, e) => ApplySelectedResult();
        right.Controls.Add(lbResults, 0, 1);
        body.Controls.Add(right, 1, 0);
        t.Controls.Add(body, 0, 1);

        var bottom = Grid(2, 3);
        Cols(bottom, (SizeType.Percent, 48), (SizeType.Percent, 52));
        Rows(bottom, Abs(38), Abs(44), Abs(26));
        var lblRun = Caption("Запустить автоподбор:", Fonts.Body, Muted, 38);
        lblRun.Dock = DockStyle.Fill;
        lblRun.TextAlign = ContentAlignment.BottomLeft;
        bottom.Controls.Add(lblRun, 0, 0);

        var bf = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Bg, Padding = Ui.Pad(0, 5, 0, 0), WrapContents = false, Margin = Padding.Empty };
        btnAuto = Btn("Быстрый", Accent, 130, 34);
        btnDeep = Btn("Глубокий", Accent, 130, 34);
        btnAutoCancel = Btn("Остановить", Danger, 130, 34);
        btnApplyBest = Btn("Выбрать выделенную стратегию", Line, 290, 34);
        btnAuto.Margin = btnDeep.Margin = btnAutoCancel.Margin = Ui.Pad(0, 0, 10, 0);
        btnAutoCancel.Enabled = false;
        tip.SetToolTip(btnAuto, "Проверяет, что сайты открываются и отдают данные. Быстро.");
        tip.SetToolTip(btnDeep, "Сначала быстрая проверка всех стратегий, затем лучшие проверяются загрузкой данных, 3 повтора.\nДольше и расходует трафик (до ~15 МБ на стратегию).");
        btnAuto.Click += async (s, e) => await RunAutoAsync(false);
        btnDeep.Click += async (s, e) => await RunAutoAsync(true);
        btnAutoCancel.Click += (s, e) => autoCts?.Cancel();
        btnApplyBest.Click += (s, e) => ApplySelectedResult();
        bf.Controls.AddRange(new Control[] { btnAuto, btnDeep, btnAutoCancel });
        bottom.Controls.Add(bf, 0, 1);

        var bf2 = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Bg, Padding = Ui.Pad(0, 5, 0, 0), WrapContents = false, Margin = Padding.Empty };
        bf2.Controls.Add(btnApplyBest);
        bottom.Controls.Add(bf2, 1, 1);

        var pf = Grid(2, 1);
        Cols(pf, Abs(420), Fill());
        prog = new ThinProgress { Dock = DockStyle.Fill, Margin = Ui.Pad(0, 9, 12, 9), BackColor = Surface, ForeColor = AccentLight, Visible = false };
        lblAuto = Caption("", Fonts.Body, Muted, 26);
        lblAuto.Dock = DockStyle.Fill;
        pf.Controls.Add(prog, 0, 0);
        pf.Controls.Add(lblAuto, 1, 0);
        bottom.SetColumnSpan(pf, 2);
        bottom.Controls.Add(pf, 0, 2);
        t.Controls.Add(bottom, 0, 2);

        p.Controls.Add(t);
        return p;
    }

    void SetAutoAdvanced(bool on)
    {
        if (autoLeft == null) return;
        autoLeft.RowStyles[3].Height = on ? Ui.S(40) : 0;
        autoAdvRow.Visible = on;
    }

    List<string> ParseDomains(string text)
    {
        var res = new List<string>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            string d = raw.Trim().ToLowerInvariant();
            if (d.Length == 0 || d.StartsWith("#")) continue;
            d = Regex.Replace(d, @"^https?://", "");
            int slash = d.IndexOf('/');
            if (slash >= 0) d = d.Substring(0, slash);
            if (d.Length > 0 && !res.Contains(d)) res.Add(d);
        }
        return res;
    }

    HashSet<string> LoadDomainSet(params string[] files)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            string path = Path.Combine(cfg.ZapretPath, "lists", f);
            if (!File.Exists(path)) continue;
            foreach (var line in File.ReadLines(path))
            {
                string t = line.Trim();
                if (t.Length > 0 && !t.StartsWith("#")) set.Add(t.ToLowerInvariant());
            }
        }
        return set;
    }

    /// <summary>Домен покрыт списком, если в нём есть он сам или любой его родительский домен.</summary>
    static bool Covered(string domain, HashSet<string> set)
    {
        var parts = domain.Split('.');
        for (int i = 0; i < parts.Length - 1; i++)
            if (set.Contains(string.Join(".", parts.Skip(i)))) return true;
        return false;
    }

    /// <summary>Дописывает домены в list-general-user.txt. true — если всё в порядке, в том числе когда добавлять нечего.</summary>
    bool AddDomainsToUserList(List<string> domains, bool silent)
    {
        if (!Strategy.IsValidZapretDir(cfg.ZapretPath)) { ShowPage(PgSettings); return false; }
        if (domains.Count == 0)
        {
            if (!silent) MessageBox.Show("Сначала впишите свои домены в поле «Свои домены для проверки».", AppSettings.AppName);
            return false;
        }
        try
        {
            Strategy.EnsureUserLists(cfg.ZapretPath);
            string path = Path.Combine(cfg.ZapretPath, "lists", "list-general-user.txt");
            var ed = editors.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
            if (ed != null && ed.Dirty)
            {
                MessageBox.Show("В списке «list-general-user.txt» есть несохранённые правки. Сначала сохраните или отмените их на вкладке «Списки».", AppSettings.AppName);
                return false;
            }
            var existing = new HashSet<string>(File.ReadAllLines(path).Select(l => l.Trim().ToLowerInvariant()));
            var add = domains.Where(d => !existing.Contains(d)).ToList();
            if (add.Count == 0)
            {
                if (!silent) MessageBox.Show("Все эти домены уже есть в вашем списке.", AppSettings.AppName);
                return true;
            }
            string cur = File.ReadAllText(path);
            string nl = cur.EndsWith("\n") || cur.Length == 0 ? "" : "\r\n";
            File.WriteAllText(path, cur + nl + string.Join("\r\n", add) + "\r\n", new UTF8Encoding(false));
            AddLog("В list-general-user.txt добавлено: " + string.Join(", ", add));
            if (ed != null) LoadEditor(ed);
            if (!silent) MessageBox.Show("Добавлено в «Свой список: домены для обхода»: " + add.Count + ".\nЧтобы применилось, перезапустите Zapret.", AppSettings.AppName);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось: " + ex.Message, AppSettings.AppName);
            return false;
        }
    }

    /// <summary>Домены из «Свой список: домены для обхода» (без заглушки и комментариев) добавляются в поле доменов для проверки.</summary>
    void ImportUserDomains()
    {
        if (!Strategy.IsValidZapretDir(cfg.ZapretPath)) { ShowPage(PgSettings); return; }
        string path = Path.Combine(cfg.ZapretPath, "lists", "list-general-user.txt");
        List<string> found;
        try { found = File.Exists(path) ? ParseDomains(File.ReadAllText(path)).Where(d => d != "domain.example.abc").ToList() : new List<string>(); }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось прочитать список: " + ex.Message, AppSettings.AppName);
            return;
        }

        var have = ParseDomains(txtCustom.Text);
        var add = found.Where(d => !have.Contains(d)).ToList();
        if (add.Count == 0)
        {
            MessageBox.Show(found.Count == 0 ? "В «Своём списке: домены для обхода» пока нет своих доменов." : "Все домены из ваших списков уже есть в поле.", AppSettings.AppName);
            return;
        }
        string cur = txtCustom.Text;
        string nl = cur.Length == 0 || cur.EndsWith("\n") ? "" : Environment.NewLine;
        txtCustom.Text = cur + nl + string.Join(Environment.NewLine, add);
    }

    void SetAutoBusy(bool busy)
    {
        autoRunning = busy;
        btnAuto.Enabled = btnDeep.Enabled = !busy;
        btnAutoCancel.Enabled = busy;
        btnApplyBest.Enabled = !busy;
        btnPower.Enabled = !busy;
        btnUpdate.Enabled = btnUpdateSettings.Enabled = !busy && !updating;
        cbStrategy.Enabled = !busy;
        txtCustom.ReadOnly = busy;
        chkStopFirst.Enabled = !busy;
        cbCand.Enabled = cbSpeed.Enabled = !busy;
        foreach (var c in chkServices) c.Enabled = !busy;
        btnAddToList.Enabled = btnImportDomains.Enabled = !busy;
        prog.Visible = busy;
        RefreshTray();
    }

    List<Target> BuildTargets(List<string> custom)
    {
        var list = new List<Target>();
        for (int i = 0; i < chkServices.Length; i++)
        {
            if (!chkServices[i].Checked) continue;
            var svc = TestTargets.Services[i];
            foreach (var d in svc.Domains) list.Add(new Target { Domain = d, Group = svc.Short });
        }
        foreach (var d in custom)
            if (!list.Any(x => x.Domain == d)) list.Add(new Target { Domain = d, Group = null });
        return list;
    }

    List<DeepService> BuildDeepServices(List<string> custom)
    {
        var list = new List<DeepService>();
        for (int i = 0; i < chkServices.Length; i++)
        {
            if (!chkServices[i].Checked) continue;
            var svc = TestTargets.Services[i];
            if (DeepProbes.Table.TryGetValue(svc.Short, out var specs)) list.Add(new DeepService { Short = svc.Short, Name = svc.Name, Specs = specs });
        }
        foreach (var d in custom) list.Add(DeepProbes.ForDomain(d));
        return list;
    }

    TestResult MakeResult(string name, Strategy st, int order, List<(Target Target, bool Ok)> checks)
    {
        var res = new TestResult { Name = name, Strategy = st, Order = order, Total = checks.Count, Ok = checks.Count(x => x.Ok) };
        foreach (var item in checks)
        {
            if (item.Ok) continue;
            if (item.Target.Group == null) res.FailedCustom.Add(item.Target.Domain);
            else if (!res.FailedGroups.Contains(item.Target.Group)) res.FailedGroups.Add(item.Target.Group);
        }
        return res;
    }

    async Task RunAutoAsync(bool deep)
    {
        if (!Strategy.IsValidZapretDir(cfg.ZapretPath)) { ShowPage(PgSettings); MessageBox.Show("Сначала укажите папку Zapret.", AppSettings.AppName); return; }
        if (strategies.Count == 0) { MessageBox.Show("В папке нет стратегий (general*.bat).", AppSettings.AppName); return; }
        if (restarting || diagRunning) { MessageBox.Show("Подождите: сейчас идёт перезапуск Zapret или диагностика.", AppSettings.AppName); return; }

        var custom = ParseDomains(txtCustom.Text);
        var targets = BuildTargets(custom);
        if (targets.Count == 0) { MessageBox.Show("Отметьте хотя бы один сервис или впишите свои домены.", AppSettings.AppName); return; }
        var deepSvcs = deep ? BuildDeepServices(custom) : null;

        cfg.TestServices = TestTargets.Services.Where((svc, idx) => chkServices[idx].Checked).Select(svc => svc.Name).ToList();
        cfg.CustomDomains = custom;
        cfg.StopOnFirstSuccess = chkStopFirst.Checked;
        cfg.AutoMode = deep ? "deep" : "quick";
        cfg.Save();

        if (deep)
        {
            int k = cfg.DeepCandidates;
            int t1 = (int)Math.Ceiling(strategies.Count * 8 / 60.0);
            int t2 = (int)Math.Ceiling(k * DeepRepeats * 10 / 60.0), t2max = (int)Math.Ceiling(k * DeepRepeats * 36 / 60.0);
            var w = MessageBox.Show(
                "Глубокий режим проверяет в два этапа.\n\n" +
                "Этап 1: быстрая проверка всех стратегий (" + strategies.Count + " шт.), около " + t1 + " мин.\n" +
                "Этап 2: до " + k + " лучших стратегий проверяются загрузкой данных, " + DeepRepeats + " повтора. Обычно " + t2 + " мин, при сбоях до " + t2max + " мин.\n\n" +
                "Трафик: до ~15 МБ на каждую стратегию на этапе 2 (до " + k * 15 + " МБ всего). На ограниченном или платном интернете осторожно.\n\n" +
                "Начать?", "Глубокий автоподбор", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (w != DialogResult.Yes) return;
        }

        if (custom.Count > 0)
        {
            var known = LoadDomainSet("list-general.txt", "list-general-user.txt", "list-google.txt");
            var missing = custom.Where(d => !Covered(d, known)).ToList();
            if (missing.Count > 0)
            {
                var ask = MessageBox.Show(
                    "Этих доменов нет в списках обхода:\n" + string.Join(", ", missing) +
                    "\n\nБез этого Zapret их не обрабатывает, и проверка покажет, что они не открываются ни на одной стратегии.\n\n" +
                    "Добавить их в «Свой список: домены для обхода»?",
                    "Автоподбор", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (ask == DialogResult.Cancel) return;
                if (ask == DialogResult.Yes && !AddDomainsToUserList(missing, true)) return;
            }
        }

        if (!ResolveConflicts(false)) return;
        var route = await Task.Run(() => SystemTools.InspectDefaultRoute());
        if (route != null && route.IsVpn)
        {
            var vr = MessageBox.Show("Похоже, интернет идёт через VPN-адаптер «" + route.Name + "».\nТест через VPN покажет не то, что вам нужно. Продолжить всё равно?",
                "Автоподбор", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (vr != DialogResult.Yes) return;
        }

        if (runner.IsRunning) StopBypass();
        autoCts = new CancellationTokenSource();
        var ct = autoCts.Token;
        results = new List<TestResult>();
        lbResults.SetItems(new string[0]);
        SetAutoBusy(true);
        prog.Maximum = strategies.Count + 1;
        prog.Value = 0;
        Cursor = Cursors.AppStarting;
        string stage = deep ? "Этап 1 из 2: " : "";

        try
        {
            EnsureTimestamps();
            cfg.DriverMayLinger = true;
            cfg.Save();

            lblAuto.Text = stage + "проверка без Zapret…";
            var baseline = await Tester.Check(targets, ct);
            var baseRes = MakeResult("(без Zapret)", null, -1, baseline);
            results.Add(baseRes);
            prog.Value = 1;
            RenderResults(false);
            if (baseRes.Ok == baseRes.Total) AddLog("Автоподбор: без Zapret всё из списка и так открывается — проверьте, не включён ли VPN.");

            bool stopFirst = !deep && cfg.StopOnFirstSuccess;
            int n = 0;
            foreach (var st in strategies)
            {
                ct.ThrowIfCancellationRequested();
                n++;
                lblAuto.Text = stage + "(" + n + "/" + strategies.Count + ") " + st.Name + "…";
                TestResult res;
                if (!runner.Start(cfg.ZapretPath, st, GameMode(), out string err))
                {
                    AddLog("Автоподбор: «" + st.Name + "» не запустилась: " + err);
                    res = new TestResult { Name = st.Name, Strategy = st, Ok = 0, Total = targets.Count, Note = "не запустилась", Order = n };
                }
                else
                {
                    await Task.Delay(800, ct);
                    var checks = await Tester.Check(targets, ct);
                    res = MakeResult(st.Name, st, n, checks);
                    runner.Stop(false);
                }
                results.Add(res);
                prog.Value = n + 1;
                RenderResults(false);

                if (stopFirst && res.Strategy != null && res.Ok == res.Total && res.Total > 0)
                {
                    lblAuto.Text = "Готово: найдена стратегия, при которой открылось всё — " + st.Name + ".";
                    prog.Value = prog.Maximum;
                    return;
                }
            }

            if (deep) await DeepStageAsync(deepSvcs, ct);
            lblAuto.Text = "Готово.";
        }
        catch (OperationCanceledException)
        {
            lblAuto.Text = "Остановлено.";
        }
        catch (Exception ex)
        {
            lblAuto.Text = "Ошибка: " + ex.Message;
            AddLog("Автоподбор: " + ex);
        }
        finally
        {
            runner.Stop(true);
            cfg.DriverMayLinger = false;
            RestoreTimestamps();
            Cursor = Cursors.Default;
            SetAutoBusy(false);
            RenderResults(true);
            SaveLastAuto(deep);
            UpdateUi();
            if (!ct.IsCancellationRequested)
                Notify("auto-done", false, "Автоподбор завершён", lblAuto.Text, ToolTipIcon.Info, () => ShowTab(PgAuto));
        }
    }

    async Task DeepStageAsync(List<DeepService> svcs, CancellationToken ct)
    {
        int k = cfg.DeepCandidates;
        var quick = results.Where(r => r.Strategy != null && !(r is DeepResult)).ToList();
        var cands = quick.Where(r => r.Total > 0 && r.Ok == r.Total).OrderBy(r => r.Order).Take(k).ToList();
        if (cands.Count == 0)
        {
            var partial = quick.Where(r => r.Ok > 0).OrderByDescending(r => r.Ok).ThenBy(r => r.Order).ToList();
            var ask = MessageBox.Show(
                "Ни одна стратегия не открыла всё.\n\n" +
                "Да — проверить глубоко лучшие частично работающие (до " + k + " шт.).\n" +
                "Нет — проверить все стратегии (" + quick.Count + " шт.): это намного дольше и расходует больше трафика.\n" +
                "Отмена — закончить на результатах быстрой проверки.",
                "Глубокий автоподбор", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (ask == DialogResult.Cancel) return;
            cands = ask == DialogResult.Yes ? partial.Take(k).ToList() : quick;
            if (cands.Count == 0) { AddLog("Автоподбор: нет стратегий для глубокой проверки."); return; }
        }

        prog.Maximum += cands.Count * DeepRepeats;
        for (int i = 0; i < cands.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var st = cands[i].Strategy;
            int ci = i + 1;
            lblAuto.Text = "Этап 2 из 2: " + st.Name + " (" + ci + "/" + cands.Count + "), запуск…";
            if (!runner.Start(cfg.ZapretPath, st, GameMode(), out string err))
            {
                AddLog("Автоподбор: «" + st.Name + "» не запустилась на этапе 2: " + err);
                prog.Value = Math.Min(prog.Maximum, prog.Value + DeepRepeats);
                continue;
            }
            await Task.Delay(800, ct);
            var dr = await DeepTester.EvaluateAsync(st, cands[i].Order, svcs, cfg.SpeedThresholdKB, DeepRepeats, r =>
            {
                lblAuto.Text = "Этап 2 из 2: " + st.Name + " (" + ci + "/" + cands.Count + "), повтор " + r + " из " + DeepRepeats;
                prog.Value = Math.Min(prog.Maximum, prog.Value + 1);
            }, ct);
            runner.Stop(false);

            int idx = results.FindIndex(x => x.Order == dr.Order && x.Strategy == st);
            if (idx >= 0) results[idx] = dr; else results.Add(dr);
            AddLog("Автоподбор (глубокий): " + dr.Row);
            RenderResults(false);
        }
        prog.Value = prog.Maximum;
    }

    void SaveLastAuto(bool deep)
    {
        var best = resultView.FirstOrDefault(x => x.Strategy != null);
        if (best == null) return;
        cfg.LastAutoAt = DateTime.Now.ToString("o");
        cfg.LastAutoResult = (deep ? "deep: " : "quick: ") + RowText(best);
        cfg.Save();
    }

    static string RowText(TestResult r)
    {
        if (r is DeepResult d) return d.Row;
        string mark = r.Strategy == null ? "·" : (r.Ok == r.Total ? "✓" : (r.Ok == 0 ? "✗" : "~"));
        var bad = new List<string>();
        bad.AddRange(r.FailedGroups);
        bad.AddRange(r.FailedCustom);
        string tail = !string.IsNullOrEmpty(r.Note) ? "   — " + r.Note : (bad.Count > 0 ? "   ✗ " + string.Join(", ", bad) : "");
        return mark + " " + r.Ok + "/" + r.Total + "  " + r.Name + tail;
    }

    void RenderResults(bool sorted)
    {
        var list = sorted
            ? results.OrderByDescending(x => x is DeepResult).ThenByDescending(x => (x as DeepResult)?.Rank ?? 0)
                     .ThenByDescending(x => x.Ok).ThenBy(x => x.Order).ToList()
            : results.OrderBy(x => x.Order).ToList();
        resultView = list;
        lbResults.SetItems(list.Select(RowText));
        if (sorted && list.Count > 0)
        {
            int best = list.FindIndex(x => x.Strategy != null);
            if (best >= 0) lbResults.SelectedIndex = best;
        }
    }

    void ApplySelectedResult()
    {
        int i = lbResults.SelectedIndex;
        if (i < 0 || i >= resultView.Count || resultView[i].Strategy == null) return;
        string name = resultView[i].Name;
        int idx = strategies.FindIndex(x => x.Name == name);
        if (idx < 0) return;
        cbStrategy.SelectedIndex = idx;
        lblAuto.Text = "Выбрана стратегия: " + name + ". Нажмите «Включить».";
    }
}
