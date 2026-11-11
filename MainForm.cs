using System.Diagnostics;
using Microsoft.Win32;

namespace ZapretControlApp;

public partial class MainForm : Form
{
    const string GameFilterHelp =
        "Игровой фильтр\n" +
        "\n" +
        "Обычно zapret обрабатывает только веб-трафик и голос Discord.\n" +
        "Многие онлайн-игры ходят по другим портам (1024–65535),\n" +
        "и по умолчанию zapret их не трогает.\n" +
        "\n" +
        "Если включить фильтр, zapret начнёт обрабатывать трафик\n" +
        "на всех этих портах. Это нужно, только если игре или её серверам\n" +
        "реально требуется обход блокировки.\n" +
        "\n" +
        "• Для YouTube, Discord и сайтов фильтр НЕ нужен.\n" +
        "• TCP и UDP — обрабатывать всё. Только TCP / только UDP —\n" +
        "  если знаете, какой протокол использует игра.\n" +
        "• Лишняя обработка может чуть увеличить пинг и иногда\n" +
        "  мешать другим программам.\n" +
        "\n" +
        "Если игры работают нормально — оставьте «выкл».";

    readonly AppSettings cfg;
    readonly Runner runner = new Runner();
    List<Strategy> strategies = new List<Strategy>();
    readonly bool startHidden;
    bool allowShow = true;
    bool shuttingDown;
    bool loading = true;
    bool warnDismissed;
    bool warnBusy;
    string lastRouteLog;

    TableLayoutPanel root, pnlWarn, footerGrid;
    string newVersionUrl;
    RoundButton btnPower, btnGameHelp, btnTray, btnUpdate, btnGitHub, btnWarnRefresh, btnWarnClose;
    Label lblStatus, lblSub, lblWarn, lblFooter, lblNewVersion;
    Dropdown cbStrategy, cbGame, cbTheme;
    TabStrip tabs;
    Panel[] pages;
    ScrollText txtLog;
    ToolTip tip;
    NotifyIcon tray;
    ToolStripMenuItem miToggle;
    System.Windows.Forms.Timer watchTimer;

    class Opt
    {
        public string Text, Value;
        public override string ToString() => Text;
    }

    public MainForm(bool autostart)
    {
        cfg = AppSettings.Load();
        Theme.Current = Theme.ByName(cfg.Theme);
        startHidden = autostart && cfg.StartInTray;

        BuildUi();
        BuildTray();
        _ = Handle;   // winws пишет в журнал из своих потоков, даже если окно скрыто
        RefreshThemeBits();
        Application.AddMessageFilter(new WheelFilter());

        runner.Log += AddLog;
        runner.Stopped += () => SafeInvoke(OnRunnerStopped);
        SystemEvents.SessionEnding += (s, e) => Shutdown();

        StartupCleanup();
        ReloadAll();
        UpdateUi();
        RefreshWarnings(true);

        watchTimer = new System.Windows.Forms.Timer { Interval = 6000 };
        watchTimer.Tick += (s, e) => RefreshWarnings(false);
        watchTimer.Start();

        loading = false;

        if (startHidden) allowShow = false;
        if (cfg.StartBypassOnLaunch && StrategyReady()) StartBypass(true);

        _ = Task.Run(async () =>
        {
            string url = await AppUpdate.CheckAsync();
            if (url != null) SafeInvoke(() => ShowNewVersion(url));
        });

        if (cfg.AutoStartApp)
        {
            _ = Task.Run(() =>
            {
                string err = SystemTools.EnsureAutostart();
                if (err != null) AddLog("Автозапуск: не удалось обновить задачу планировщика: " + err);
            });
        }
    }

    bool StrategyReady() => Strategy.IsValidZapretDir(cfg.ZapretPath) && SelectedStrategy() != null;

    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(allowShow ? value : false);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetDarkTitleBar(Handle, P.Dark);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (AnyListDirty && !shuttingDown)
        {
            var r = MessageBox.Show("Есть несохранённые изменения в списках. Закрыть без сохранения?", AppSettings.AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) { e.Cancel = true; return; }
        }
        Shutdown();
        base.OnFormClosing(e);
    }

    void ShowMain()
    {
        allowShow = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    void HideToTray()
    {
        Hide();
    }

    void Shutdown()
    {
        if (shuttingDown) return;
        shuttingDown = true;
        try { autoCts?.Cancel(); } catch { }
        try { runner.Stop(true); } catch { }
        try { cfg.DriverMayLinger = false; RestoreTimestamps(); cfg.Save(); } catch { }
        try { if (tray != null) { tray.Visible = false; tray.Dispose(); } } catch { }
    }

    public void EmergencyShutdown() => Shutdown();

    /// <summary>Прошлый запуск мог упасть: драйвер и timestamps тогда остались как были во время работы.</summary>
    void StartupCleanup()
    {
        try
        {
            if (!SystemTools.AnyWinwsRunning() && !SystemTools.ZapretServiceRunning())
            {
                if (cfg.DriverMayLinger) { SystemTools.CleanupWinDivert(); cfg.DriverMayLinger = false; }
                if (cfg.TimestampsChangedByUs) RestoreTimestamps();
                cfg.Save();
            }
        }
        catch { }
    }

    void EnsureTimestamps()
    {
        if (!cfg.ManageTimestamps || cfg.TimestampsChangedByUs) return;
        if (SystemTools.TimestampsEnabled() == false)
        {
            SystemTools.SetTimestamps(true);
            cfg.TimestampsChangedByUs = true;
            cfg.Save();
            AddLog("TCP timestamps включены на время работы (при выходе вернутся).");
        }
    }

    void RestoreTimestamps()
    {
        if (!cfg.TimestampsChangedByUs) return;
        SystemTools.SetTimestamps(false);
        cfg.TimestampsChangedByUs = false;
        cfg.Save();
        AddLog("TCP timestamps возвращены как были.");
    }

    Strategy SelectedStrategy() => cbStrategy?.SelectedItem as Strategy;
    string GameMode() => (cbGame?.SelectedItem as Opt)?.Value ?? "off";

    void TogglePower()
    {
        if (runner.IsRunning) StopBypass(); else StartBypass(false);
    }

    bool StartBypass(bool silent)
    {
        if (!Strategy.IsValidZapretDir(cfg.ZapretPath))
        {
            ShowPage(3);
            if (!silent) MessageBox.Show("Сначала укажите папку zapret (или скачайте его кнопкой «Обновить Zapret» внизу).", AppSettings.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        var st = SelectedStrategy();
        if (st == null)
        {
            if (!silent) MessageBox.Show("В папке не найдено ни одной стратегии (general*.bat).", AppSettings.AppName);
            return false;
        }

        if (!ResolveConflicts(silent)) return false;

        Cursor = Cursors.WaitCursor;
        try
        {
            EnsureTimestamps();
            cfg.DriverMayLinger = true;
            cfg.Save();
            if (!runner.Start(cfg.ZapretPath, st, GameMode(), out string err))
            {
                AddLog("Не удалось запустить: " + err);
                if (!silent) MessageBox.Show(err, "Не удалось включить обход", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Runner.CleanupDriver();
                RestoreTimestamps();
                UpdateUi();
                return false;
            }
            cfg.Strategy = st.Name;
            cfg.Save();
            AddLog("Обход включён: " + st.Name + " (PID " + runner.Pid + ")");
        }
        finally { Cursor = Cursors.Default; }
        UpdateUi();
        return true;
    }

    void StopBypass()
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            runner.Stop(true);
            cfg.DriverMayLinger = false;
            RestoreTimestamps();
            cfg.Save();
            AddLog("Обход выключен.");
        }
        finally { Cursor = Cursors.Default; }
        UpdateUi();
    }

    void RestartIfRunning()
    {
        if (runner.IsRunning) { StopBypass(); StartBypass(true); }
    }

    void OnRunnerStopped()
    {
        if (shuttingDown) return;
        AddLog("winws.exe завершился сам (не по команде). Смотрите журнал выше.");
        Runner.CleanupDriver();
        RestoreTimestamps();
        UpdateUi();
    }

    /// <summary>Другой winws или служба zapret конфликтуют с нашим запуском: предлагаем остановить.</summary>
    bool ResolveConflicts(bool silent)
    {
        var foreign = SystemTools.ForeignWinws(runner.Pid);
        bool svc = SystemTools.ZapretServiceRunning();
        if (foreign.Count == 0 && !svc) return true;

        string what = (svc ? "служба Windows «zapret» (из service.bat)" : "") +
                      (svc && foreign.Count > 0 ? " и " : "") +
                      (foreign.Count > 0 ? "другой процесс winws.exe (" + foreign.Count + " шт.)" : "");
        if (silent)
        {
            AddLog("Конфликт: уже запущена " + what + ". Автозапуск обхода пропущен.");
            foreach (var p in foreign) p.Dispose();
            return false;
        }

        var r = MessageBox.Show("Уже работает " + what + ".\nДва обхода одновременно конфликтуют.\n\nОстановить их и продолжить?",
            "Конфликт обходов", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (r != DialogResult.Yes)
        {
            foreach (var p in foreign) p.Dispose();
            return false;
        }

        if (svc) SystemTools.Run("sc.exe", "stop zapret", 15000);
        foreach (var p in foreign)
        {
            try { p.Kill(true); p.WaitForExit(3000); } catch { }
            p.Dispose();
        }
        Thread.Sleep(500);
        return true;
    }

    async void RefreshWarnings(bool force)
    {
        if (shuttingDown || pnlWarn == null || warnBusy) return;
        if (warnDismissed && !force) return;
        warnBusy = true;
        try
        {
            int ownPid = runner.Pid;
            var data = await Task.Run(() => (Foreign: SystemTools.ForeignWinwsCount(ownPid), Route: SystemTools.InspectDefaultRoute()));
            if (shuttingDown) return;

            var msgs = new List<string>();
            if (data.Foreign > 0)
                msgs.Add("Запущен другой winws.exe (" + data.Foreign + "): он перехватывает тот же трафик, обход может работать неправильно.");
            if (data.Route != null)
            {
                string desc = data.Route.Name + " | " + data.Route.Description + " | тип " + data.Route.Type +
                              " | аппаратный: " + (data.Route.Hardware.HasValue ? (data.Route.Hardware.Value ? "да" : "нет") : "неизвестно") +
                              " | VPN: " + (data.Route.IsVpn ? "да" : "нет");
                if (desc != lastRouteLog) { lastRouteLog = desc; AddLog("Маршрут в интернет: " + desc); }
                if (data.Route.IsVpn)
                    msgs.Add("Похоже, весь интернет идёт через VPN-адаптер «" + data.Route.Name + "». В режиме TUN он может конфликтовать с zapret.");
            }

            if (force) warnDismissed = false;
            bool show = msgs.Count > 0;
            lblWarn.Text = show ? string.Join("   ", msgs) : "";
            root.RowStyles[1].Height = show ? Ui.S(52) : 0;
            pnlWarn.Visible = show;
        }
        catch { }
        finally { warnBusy = false; }
    }

    void BuildUi()
    {
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.None;
        Text = AppSettings.AppName;
        ClientSize = Ui.Sz(960, 700);
        MinimumSize = Ui.Sz(940, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = TextC;
        Font = Fonts.Body;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath); } catch { }
        tip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 150, ReshowDelay = 100 };

        root = Grid(1, 5);
        Cols(root, Fill());
        Rows(root, Abs(132), Abs(0), Abs(42), Fill(), Abs(48));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildWarnBar(), 0, 1);
        root.Controls.Add(BuildTabBar(), 0, 2);

        var content = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Bg };
        pages = new[] { BuildPageLog(), BuildPageLists(), BuildPageAuto(), BuildPageSettings() };
        foreach (var p in pages) { p.Dock = DockStyle.Fill; content.Controls.Add(p); }
        root.Controls.Add(content, 0, 3);

        root.Controls.Add(BuildFooter(), 0, 4);

        Controls.Add(root);
        ShowPage(0);
        ResumeLayout(true);
    }

    Control BuildHeader()
    {
        var h = Grid(2, 1);
        h.BackColor = Surface;
        h.Padding = Ui.Pad(Ui.Margin);
        Cols(h, Abs(186), Fill());

        btnPower = Btn("Включить", Accent, 170, 100);
        btnPower.Dock = DockStyle.Fill;
        btnPower.Margin = Ui.Pad(0, 0, 16, 0);
        btnPower.Font = Fonts.Display;
        btnPower.Click += (s, e) => TogglePower();
        h.Controls.Add(btnPower, 0, 0);

        var info = Grid(2, 3);
        info.BackColor = Surface;
        Cols(info, Fill(), Abs(152));
        Rows(info, Abs(36), Abs(26), Fill());

        lblStatus = Caption("Zapret выключен", Fonts.Display, Muted, 36);
        lblStatus.Dock = DockStyle.Fill;
        info.Controls.Add(lblStatus, 0, 0);

        btnTray = Btn("Свернуть в трей", Line, 140, 34);
        btnTray.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnTray.Click += (s, e) => HideToTray();
        info.Controls.Add(btnTray, 1, 0);

        lblSub = Caption("", Fonts.Small, Muted, 26);
        lblSub.Dock = DockStyle.Fill;
        info.Controls.Add(lblSub, 0, 1);
        info.SetColumnSpan(lblSub, 2);

        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Surface, Margin = Padding.Empty, Padding = Ui.Pad(0, 4, 0, 0), WrapContents = false };
        var l1 = Lbl("Стратегия", Muted);
        l1.Margin = Ui.Pad(0, 7, 10, 0);
        cbStrategy = Drop(280);
        cbStrategy.Margin = Ui.Pad(0, 0, 14, 0);
        cbStrategy.SelectedIndexChanged += (s, e) =>
        {
            if (loading) return;
            var st = SelectedStrategy();
            if (st != null) { cfg.Strategy = st.Name; cfg.Save(); }
            RestartIfRunning();
        };

        cbGame = Drop(250);
        cbGame.Margin = Ui.Pad(0, 0, 10, 0);
        cbGame.Items.Add(new Opt { Text = "Игровой фильтр: выкл", Value = "off" });
        cbGame.Items.Add(new Opt { Text = "Игровой фильтр: TCP и UDP", Value = "all" });
        cbGame.Items.Add(new Opt { Text = "Игровой фильтр: только TCP", Value = "tcp" });
        cbGame.Items.Add(new Opt { Text = "Игровой фильтр: только UDP", Value = "udp" });
        int gi = cbGame.Items.Cast<Opt>().ToList().FindIndex(o => o.Value == cfg.GameFilter);
        cbGame.SelectedIndex = gi < 0 ? 0 : gi;
        cbGame.SelectedIndexChanged += (s, e) =>
        {
            if (loading) return;
            cfg.GameFilter = GameMode();
            cfg.Save();
            RestartIfRunning();
        };

        btnGameHelp = Btn("?", Line, 34, 34);
        tip.SetToolTip(btnGameHelp, GameFilterHelp);
        btnGameHelp.Click += (s, e) => MessageBox.Show(GameFilterHelp, "Игровой фильтр", MessageBoxButtons.OK, MessageBoxIcon.Information);

        row.Controls.AddRange(new Control[] { l1, cbStrategy, cbGame, btnGameHelp });
        info.Controls.Add(row, 0, 2);
        info.SetColumnSpan(row, 2);
        h.Controls.Add(info, 1, 0);
        return h;
    }

    Control BuildWarnBar()
    {
        pnlWarn = Grid(3, 1);
        pnlWarn.BackColor = WarnBg;
        pnlWarn.Visible = false;
        Cols(pnlWarn, Fill(), Abs(116), Abs(112));

        lblWarn = Caption("", Fonts.Body, WarnText, 52);
        lblWarn.Dock = DockStyle.Fill;
        lblWarn.Padding = Ui.Pad(Ui.Margin, 0, 8, 0);

        btnWarnRefresh = Btn("Обновить", WarnBtn, 104, 34);
        btnWarnRefresh.Anchor = AnchorStyles.None;
        btnWarnRefresh.Click += (s, e) => RefreshWarnings(true);
        btnWarnClose = Btn("Закрыть", WarnBtn, 96, 34);
        btnWarnClose.Anchor = AnchorStyles.None;
        btnWarnClose.Click += (s, e) =>
        {
            warnDismissed = true;
            pnlWarn.Visible = false;
            root.RowStyles[1].Height = 0;
        };

        pnlWarn.Controls.Add(lblWarn, 0, 0);
        pnlWarn.Controls.Add(btnWarnRefresh, 1, 0);
        pnlWarn.Controls.Add(btnWarnClose, 2, 0);
        return pnlWarn;
    }

    Control BuildTabBar()
    {
        var t = Grid(2, 1);
        Cols(t, Fill(), Abs(250));

        tabs = new TabStrip { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Bg };
        tabs.Tabs.AddRange(new[] { "Журнал", "Списки", "Проверка стратегий (автоподбор)", "Настройки" });
        tabs.SelectedChanged += (s, e) => ShowPage(tabs.Selected);
        t.Controls.Add(tabs, 0, 0);

        var right = new LinePanel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Bg };
        var lt = Lbl("Тема", Muted);
        lt.Location = new Point(Ui.S(40), Ui.S(11));
        cbTheme = Drop(150);
        cbTheme.Location = new Point(Ui.S(84), Ui.S(4));
        cbTheme.Items.Add(new Opt { Text = "Светлая", Value = "light" });
        cbTheme.Items.Add(new Opt { Text = "Тёмная", Value = "dark" });
        cbTheme.Items.Add(new Opt { Text = "Зелёная", Value = "green" });
        int ti = cbTheme.Items.Cast<Opt>().ToList().FindIndex(o => o.Value == cfg.Theme);
        cbTheme.SelectedIndex = ti < 0 ? 1 : ti;
        cbTheme.SelectedIndexChanged += (s, e) =>
        {
            if (loading) return;
            if (cbTheme.SelectedItem is Opt o) ApplyTheme(o.Value, true);
        };
        right.Controls.Add(lt);
        right.Controls.Add(cbTheme);
        t.Controls.Add(right, 1, 0);
        return t;
    }

    Control BuildFooter()
    {
        footerGrid = Grid(4, 1);
        footerGrid.BackColor = Surface;
        footerGrid.Padding = Ui.Pad(Ui.Margin, 7, Ui.Margin, 7);
        Cols(footerGrid, Abs(192), Fill(), Abs(0), Abs(176));

        btnUpdate = Btn("Обновить Zapret", Accent, 180, 34);
        btnUpdate.Dock = DockStyle.Fill;
        btnUpdate.Margin = Ui.Pad(0, 0, 12, 0);
        btnUpdate.Click += async (s, e) => await UpdateZapretAsync();

        lblFooter = Caption("", Fonts.Small, Muted, 34);
        lblFooter.Dock = DockStyle.Fill;
        lblFooter.AutoEllipsis = true;

        lblNewVersion = Caption("Вышла новая версия! Скачать — жми", Fonts.Body, DangerText, 34);
        lblNewVersion.Dock = DockStyle.Fill;
        lblNewVersion.TextAlign = ContentAlignment.MiddleRight;
        lblNewVersion.Margin = Ui.Pad(0, 0, 14, 0);
        lblNewVersion.Cursor = Cursors.Hand;
        lblNewVersion.Visible = false;
        lblNewVersion.Click += (s, e) => OpenUrl(newVersionUrl ?? AppUpdate.RepoUrl);

        btnGitHub = Btn("GitHub  " + AppUpdate.Format(AppUpdate.Current), Line, 170, 34);
        btnGitHub.Dock = DockStyle.Fill;
        btnGitHub.Click += (s, e) => OpenUrl(AppUpdate.RepoUrl);

        footerGrid.Controls.Add(btnUpdate, 0, 0);
        footerGrid.Controls.Add(lblFooter, 1, 0);
        footerGrid.Controls.Add(lblNewVersion, 2, 0);
        footerGrid.Controls.Add(btnGitHub, 3, 0);
        return footerGrid;
    }

    void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { AddLog("Не удалось открыть ссылку: " + ex.Message); }
    }

    void ShowNewVersion(string url)
    {
        if (shuttingDown || IsDisposed) return;
        newVersionUrl = url;
        footerGrid.ColumnStyles[2].Width = Ui.S(300);
        lblNewVersion.Visible = true;
    }

    void ShowPage(int i)
    {
        if (tabs.Selected != i) { tabs.Selected = i; return; }
        for (int k = 0; k < pages.Length; k++) pages[k].Visible = k == i;
        if (i == 1 && !listsLoaded) ReloadListFiles(false);
    }

    Panel BuildPageLog()
    {
        var p = Page();
        txtLog = Input(true);
        txtLog.Dock = DockStyle.Fill;
        txtLog.ReadOnly = true;
        txtLog.WordWrap = true;
        p.Controls.Add(txtLog);
        return p;
    }

    void AddLog(string s)
    {
        if (s == null) return;
        if (InvokeRequired) { SafeInvoke(() => AddLog(s)); return; }
        if (txtLog == null) return;
        txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + Environment.NewLine);
        if (txtLog.TextLength > 200000) txtLog.Text = txtLog.Text.Substring(txtLog.TextLength - 100000);
    }

    void SafeInvoke(Action a)
    {
        try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { }
    }

    void UpdateUi()
    {
        if (IsDisposed) return;
        bool on = runner.IsRunning;
        btnPower.Text = on ? "Выключить" : "Включить";
        btnPower.BackColor = on ? Danger : Accent;
        btnPower.ForeColor = Color.White;
        lblStatus.Text = on ? "Zapret включён" : "Zapret выключен";
        lblStatus.ForeColor = on ? AccentLight : Muted;
        lblSub.Text = on
            ? "Стратегия: " + runner.CurrentStrategy + "   ·   PID " + runner.Pid
            : (Strategy.IsValidZapretDir(cfg.ZapretPath) ? "Выберите стратегию и нажмите «Включить»" : "Сначала укажите папку zapret на вкладке «Настройки»");
        if (miToggle != null) miToggle.Text = on ? "Выключить обход" : "Включить обход";
        if (tray != null) tray.Text = on ? AppSettings.AppName + " — работает" : AppSettings.AppName + " — выключен";
        lblFooter.Text = string.IsNullOrEmpty(cfg.ZapretPath) ? "Папка zapret не выбрана" : "Папка zapret: " + cfg.ZapretPath;
    }

    void BuildTray()
    {
        var menu = new ContextMenuStrip { Renderer = new ThemeRenderer(), Font = Fonts.Body };
        var miShow = new ToolStripMenuItem("Показать окно");
        miShow.Click += (s, e) => ShowMain();
        miToggle = new ToolStripMenuItem("Включить обход");
        miToggle.Click += (s, e) => TogglePower();
        var miExit = new ToolStripMenuItem("Выход (всё выключится)");
        miExit.Click += (s, e) => Close();
        menu.Items.AddRange(new ToolStripItem[] { miShow, miToggle, new ToolStripSeparator(), miExit });

        tray = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = AppSettings.AppName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.DoubleClick += (s, e) => ShowMain();
    }

    void ReloadAll()
    {
        bool was = loading;
        loading = true;
        try
        {
            strategies = Strategy.LoadAll(cfg.ZapretPath);
            cbStrategy.Items.Clear();
            foreach (var s in strategies) cbStrategy.Items.Add(s);
            cbStrategy.ItemsChanged();
            int idx = strategies.FindIndex(s => s.Name == cfg.Strategy);
            if (idx < 0) idx = strategies.FindIndex(s => s.Name.Equals("general", StringComparison.OrdinalIgnoreCase));
            if (idx < 0 && strategies.Count > 0) idx = 0;
            cbStrategy.SelectedIndex = idx;
            if (txtPath != null) txtPath.Text = cfg.ZapretPath;
            string v = Strategy.ReadVersion(cfg.ZapretPath);
            if (lblVersion != null)
                lblVersion.Text = Strategy.IsValidZapretDir(cfg.ZapretPath)
                    ? "Установлена версия: " + (v ?? "неизвестно") + "  ·  стратегий: " + strategies.Count
                    : "zapret в этой папке не найден";
        }
        finally { loading = was; }
        listsLoaded = false;
        if (pages != null && tabs != null && tabs.Selected == 1) ReloadListFiles(true);
    }
}
