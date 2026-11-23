namespace ZapretControlApp;

public partial class MainForm
{
    ScrollHost diagHost;
    RoundButton btnDiag, btnDiagCancel, btnDiagCopy;
    Label lblDiag;
    FitNote[] diagRows = new FitNote[0];
    CheckResult[] diagResults = new CheckResult[0];
    CancellationTokenSource diagCts;

    Panel BuildPageDiag()
    {
        var p = Page();
        var t = Grid(1, 3);
        Cols(t, Fill());
        Rows(t, Abs(50), Abs(34), Fill());

        var bf = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Bg, Padding = Ui.Pad(0, 12, 0, 0), WrapContents = false, Margin = Padding.Empty };
        btnDiag = Btn("Начать диагностику", Accent, 220, 34);
        btnDiagCancel = Btn("Отмена", Danger, 120, 34);
        btnDiagCopy = Btn("Скопировать отчёт", Line, 200, 34);
        btnDiag.Margin = btnDiagCancel.Margin = Ui.Pad(0, 0, 10, 0);
        btnDiagCancel.Enabled = btnDiagCopy.Enabled = false;
        btnDiag.Click += async (s, e) => await RunDiagAsync();
        btnDiagCancel.Click += (s, e) => diagCts?.Cancel();
        btnDiagCopy.Click += (s, e) => CopyDiagReport();
        bf.Controls.AddRange(new Control[] { btnDiag, btnDiagCancel, btnDiagCopy });
        t.Controls.Add(bf, 0, 0);

        lblDiag = Caption("Диагностика только читает состояние системы и ничего не меняет.", Fonts.Heading, Muted, 34);
        lblDiag.Dock = DockStyle.Fill;
        t.Controls.Add(lblDiag, 0, 1);

        diagHost = new ScrollHost { Dock = DockStyle.Fill, BackColor = Bg, Margin = Padding.Empty };
        diagHost.Content.BackColor = Bg;
        diagHost.Content.Controls.Add(new FitNote
        {
            BackColor = Surface, ForeColor = Muted, Font = Fonts.Body, Height = Ui.S(60),
            Text = "Нажмите «Начать диагностику»: проверятся права, папка Zapret, драйвер, конфликты, VPN и прокси, интернет, DNS, время, доступность сервисов, антивирус, «Целостность памяти» и TCP timestamps. Это займёт не больше 30 секунд.",
        });
        t.Controls.Add(diagHost, 0, 2);

        p.Controls.Add(t);
        return p;
    }

    void ColorDiagRow(FitNote n, Sev sev)
    {
        switch (sev)
        {
            case Sev.Ok: n.BackColor = Surface; n.ForeColor = TextC; break;
            case Sev.Warn: n.BackColor = WarnBg; n.ForeColor = WarnText; break;
            case Sev.Fail: n.BackColor = Surface; n.ForeColor = DangerText; break;
            default: n.BackColor = Surface; n.ForeColor = Muted; break;
        }
    }

    async Task RunDiagAsync()
    {
        if (diagRunning) return;
        if (autoRunning || updating)
        {
            MessageBox.Show("Сначала дождитесь окончания автоподбора или обновления.", AppSettings.AppName);
            return;
        }

        var targets = new List<Target>();
        foreach (var svc in TestTargets.Services)
            if (cfg.TestServices.Contains(svc.Name))
                foreach (var dom in svc.Domains) targets.Add(new Target { Domain = dom, Group = svc.Short });
        foreach (var dom in cfg.CustomDomains)
            if (!targets.Any(x => x.Domain == dom)) targets.Add(new Target { Domain = dom });

        var input = new DiagInput
        {
            ZapretPath = cfg.ZapretPath,
            StrategyName = runner.CurrentStrategy ?? SelectedStrategy()?.Name,
            WinwsTail = WinwsTailText(),
            LastStartError = lastStartError,
            BypassRunning = runner.IsRunning,
            ManageTimestamps = cfg.ManageTimestamps,
            OwnPid = runner.Pid,
            Targets = targets,
        };
        var checks = DiagRunner.Build(input);

        var host = diagHost.Content;
        host.SuspendLayout();
        foreach (var c in host.Controls.Cast<Control>().ToList()) { host.Controls.Remove(c); c.Dispose(); }
        diagRows = new FitNote[checks.Count];
        diagResults = new CheckResult[checks.Count];
        for (int i = 0; i < checks.Count; i++)
        {
            diagRows[i] = new FitNote
            {
                BackColor = Surface, ForeColor = Muted, Font = Fonts.Body, Height = Ui.S(44), Margin = Ui.Pad(0, 0, 0, 8),
                Text = "●  " + checks[i].Title + " — проверяется…",
            };
            host.Controls.Add(diagRows[i]);
        }
        host.ResumeLayout(true);

        diagRunning = true;
        btnDiag.Enabled = btnDiagCopy.Enabled = false;
        btnDiagCancel.Enabled = true;
        lblDiag.Text = "Идёт диагностика…";
        lblDiag.ForeColor = Muted;
        RefreshTray();

        diagCts = new CancellationTokenSource();
        try
        {
            await Task.Run(() => DiagRunner.RunAllAsync(checks, (i, r) => SafeInvoke(() => ShowDiagResult(i, r)), diagCts.Token));
        }
        catch (Exception ex) { Diag.Write("diag", ex); }
        finally
        {
            diagRunning = false;
            diagCts.Dispose();
            diagCts = null;
            for (int i = 0; i < diagResults.Length; i++)
                if (diagResults[i] == null) ShowDiagResult(i, new CheckResult { Title = checks[i].Title, Sev = Sev.Unknown, Detail = "Не удалось проверить: отменено." });
            btnDiag.Enabled = btnDiagCopy.Enabled = true;
            btnDiagCancel.Enabled = false;
            ShowDiagSummary();
            RefreshTray();
        }
    }

    void ShowDiagResult(int i, CheckResult r)
    {
        if (IsDisposed || i >= diagRows.Length) return;
        diagResults[i] = r;
        var n = diagRows[i];
        n.Text = r.Mark + "  " + r.Title + " — " + r.Detail + (string.IsNullOrEmpty(r.Advice) ? "" : "\nСовет: " + r.Advice);
        ColorDiagRow(n, r.Sev);
    }

    void ShowDiagSummary()
    {
        int problems = diagResults.Count(r => r != null && (r.Sev == Sev.Warn || r.Sev == Sev.Fail));
        int unknown = diagResults.Count(r => r != null && r.Sev == Sev.Unknown);
        lblDiag.Text = (problems == 0 ? "Проблем не найдено" : "Найдено проблем: " + problems) + (unknown > 0 ? "   ·   не удалось проверить: " + unknown : "");
        lblDiag.ForeColor = problems == 0 ? AccentLight : DangerText;
    }

    void CopyDiagReport()
    {
        var done = diagResults.Where(r => r != null).ToList();
        if (done.Count == 0) return;
        string header = AppSettings.AppName + " " + AppUpdate.Format(AppUpdate.Current) + " — отчёт диагностики\n" +
                        "Дата: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n" +
                        "Windows: " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (64 бит)" : "") + "\n" +
                        "Zapret: " + (runner.IsRunning ? "включён, " + runner.CurrentStrategy : "выключен") + "\n" +
                        "Итог: " + lblDiag.Text;
        try
        {
            Clipboard.SetText(DiagRunner.BuildReport(done, header));
            AddLog("Отчёт диагностики скопирован в буфер обмена.");
        }
        catch (Exception ex) { MessageBox.Show("Не удалось скопировать: " + ex.Message, AppSettings.AppName); }
    }
}
