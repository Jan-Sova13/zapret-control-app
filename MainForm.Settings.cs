namespace ZapretControlApp;

public partial class MainForm
{
    ScrollText txtPath;
    RoundButton btnBrowse, btnSavePath, btnUpdateSettings;
    TableLayoutPanel pathRow;
    Label lblZapretUpdate;

    const string NotifyHelp =
        "Уведомления Windows о событиях Zapret и значок в трее.\n\n" +
        "• Все — все события: упал, проблемы, восстановился, окончание автоподбора, обновления.\n" +
        "• Только важные — Zapret упал или перестал открывать сервисы.\n" +
        "• Выключены — уведомлений нет, значок в трее остаётся.\n" +
        "• Выключены + убрать значок из трея — уведомлений и значка нет, кнопка «Свернуть в трей» недоступна.\n\n" +
        "Уведомления не показываются, пока окно программы активно, и одно и то же событие — не чаще раза в 10 минут. Режим «Не беспокоить» в Windows учитывается.";

    RoundButton HelpBtn(string title, string text)
    {
        var b = Btn("?", Line, 34, 34);
        tip.SetToolTip(b, text);
        b.Click += (s, e) => MessageBox.Show(text, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return b;
    }
    Check chkAutoStart, chkTray, chkStartBypass, chkTimestamps, chkAutoUpdate;
    Dropdown cbNotify;
    Label lblVersion;
    readonly List<Control> advSettings = new List<Control>();

    Panel BuildPageSettings()
    {
        var p = Page();
        var host = new ScrollHost { Dock = DockStyle.Fill, BackColor = Bg, Margin = Padding.Empty };
        var c = host.Content;
        c.BackColor = Bg;
        c.SuspendLayout();

        c.Controls.Add(Caption("Папка Zapret (где лежат bin, lists, utils и general*.bat):", Fonts.Body, Muted, 28));

        var row = pathRow = Grid(3, 1);
        row.Dock = DockStyle.None;
        row.Height = Ui.S(40);
        row.Margin = Ui.Pad(0, 0, 0, 6);
        Cols(row, Fill(), Abs(140), Abs(0));
        txtPath = Input(false);
        txtPath.Dock = DockStyle.Fill;
        txtPath.Margin = Ui.Pad(0, 0, 10, 0);
        txtPath.Text = cfg.ZapretPath;
        txtPath.Box.TextChanged += (s, e) => UpdatePathEditing();
        txtPath.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { ApplyPathFromText(); e.SuppressKeyPress = true; } };
        btnBrowse = Btn("Выбрать ...", Line, 130, 34);
        btnBrowse.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        btnBrowse.Click += (s, e) =>
        {
            using var d = new FolderBrowserDialog { Description = "Папка с zapret-discord-youtube", UseDescriptionForTitle = true, SelectedPath = cfg.ZapretPath };
            if (d.ShowDialog(this) == DialogResult.OK) ApplyPath(d.SelectedPath);
        };
        btnSavePath = Btn("Сохранить", Accent, 130, 34);
        btnSavePath.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        btnSavePath.Margin = Ui.Pad(10, 0, 0, 0);
        btnSavePath.Visible = false;
        btnSavePath.Click += (s, e) => ApplyPathFromText();
        row.Controls.Add(txtPath, 0, 0);
        row.Controls.Add(btnBrowse, 1, 0);
        row.Controls.Add(btnSavePath, 2, 0);
        c.Controls.Add(row);

        lblVersion = Caption("", Fonts.Body, Muted, 28);
        c.Controls.Add(lblVersion);
        var ur = new FlowLayoutPanel { BackColor = Bg, Height = Ui.S(46), WrapContents = false, Margin = Ui.Pad(0, 2, 0, 12) };
        btnUpdateSettings = Btn("Обновить Zapret", Accent, 180, 34);
        btnUpdateSettings.Margin = Ui.Pad(0, 0, 14, 0);
        btnUpdateSettings.Click += async (s, e) => await UpdateZapretAsync();
        lblZapretUpdate = Lbl("", DangerText);
        lblZapretUpdate.Margin = Ui.Pad(0, 7, 0, 0);
        ur.Controls.AddRange(new Control[] { btnUpdateSettings, lblZapretUpdate });
        c.Controls.Add(ur);

        chkAutoStart = Chk("Запускать программу при входе в Windows");
        chkAutoStart.Checked = cfg.AutoStartApp;
        chkAutoStart.CheckedChanged += (s, e) => OnAutoStartChanged();

        chkTray = Chk("При автозапуске сворачиваться в трей");
        chkTray.Margin = Ui.Pad(30, 0, 0, 0);
        chkTray.Checked = cfg.StartInTray;
        chkTray.Enabled = cfg.AutoStartApp && cfg.Notifications != "offhide";
        chkTray.CheckedChanged += (s, e) => { if (!loading) { cfg.StartInTray = chkTray.Checked; cfg.Save(); } };

        chkStartBypass = Chk("Включать Zapret сразу при запуске программы");
        chkStartBypass.Checked = cfg.StartBypassOnLaunch;
        chkStartBypass.CheckedChanged += (s, e) => { if (!loading) { cfg.StartBypassOnLaunch = chkStartBypass.Checked; cfg.Save(); } };

        chkTimestamps = Chk("Включать TCP timestamps на время работы (при выключении возвращаются)");
        chkTimestamps.Checked = cfg.ManageTimestamps;
        chkTimestamps.CheckedChanged += (s, e) => { if (!loading) { cfg.ManageTimestamps = chkTimestamps.Checked; cfg.Save(); } };

        chkAutoUpdate = Chk("Автоматически проверять обновления программы и Zapret (при запуске и раз в сутки)");
        chkAutoUpdate.Checked = cfg.AutoCheckUpdates;
        chkAutoUpdate.CheckedChanged += (s, e) => { if (!loading) { cfg.AutoCheckUpdates = chkAutoUpdate.Checked; cfg.Save(); } };

        foreach (var chk in new[] { chkAutoStart, chkTray, chkStartBypass, chkTimestamps, chkAutoUpdate })
        {
            chk.Margin = new Padding(chk.Margin.Left, 0, 0, Ui.S(8));
            c.Controls.Add(chk);
        }
        advSettings.Add(chkTimestamps);
        advSettings.Add(chkAutoUpdate);

        cbNotify = Drop(320);
        cbNotify.Items.Add(new Opt { Text = "Все", Value = "all" });
        cbNotify.Items.Add(new Opt { Text = "Только важные", Value = "important" });
        cbNotify.Items.Add(new Opt { Text = "Выключены", Value = "off" });
        cbNotify.Items.Add(new Opt { Text = "Выключены + убрать значок из трея", Value = "offhide" });
        cbNotify.SelectedIndex = Math.Max(0, cbNotify.Items.Cast<Opt>().ToList().FindIndex(o => o.Value == cfg.Notifications));
        cbNotify.SelectedIndexChanged += (s, e) =>
        {
            if (loading || !(cbNotify.SelectedItem is Opt o)) return;
            cfg.Notifications = o.Value;
            cfg.Save();
            ApplyTrayMode();
        };
        c.Controls.Add(Caption("Уведомления и значок в трее:", Fonts.Body, Muted, 28));
        c.Controls.Add(OptionRow(null, cbNotify, NotifyHelp, "Уведомления и значок в трее"));

        var rr = new FlowLayoutPanel { BackColor = Bg, Height = Ui.S(46), WrapContents = false, Margin = Ui.Pad(0, 14, 0, 0) };
        var btnReset = Btn("Удалить все настройки", Danger, 220, 34);
        btnReset.Margin = Ui.Pad(0, 0, 14, 0);
        btnReset.Click += (s, e) => ResetAllSettings();
        var lblReset = Lbl("Удаляет файлы настроек и закрывает программу", Muted);
        lblReset.Margin = Ui.Pad(0, 7, 0, 0);
        rr.Controls.AddRange(new Control[] { btnReset, lblReset });
        c.Controls.Add(rr);
        advSettings.Add(rr);

        c.ResumeLayout(true);
        p.Controls.Add(host);
        return p;
    }

    bool resetRequested;

    void ResetAllSettings()
    {
        if (autoRunning || updating || restarting || diagRunning)
        {
            MessageBox.Show("Сначала дождитесь окончания автоподбора, диагностики или обновления.", AppSettings.AppName);
            return;
        }
        string text =
            "Будут удалены все файлы, которые программа создала в %AppData%\\Zapret Control App: настройки, журнал сбоев, бэкапы списков и временные файлы обновления, а также задача автозапуска.\n\n" +
            "Ваши списки (*-user.txt) и папка Zapret не затрагиваются.\n\n" +
            (AnyListDirty ? "Несохранённые правки в списках будут потеряны.\n\n" : "") +
            "Программа закроется. При следующем запуске будут созданы новые файлы со значениями по умолчанию.";
        if (MessageBox.Show(text, "Удалить все настройки", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        resetRequested = true;
        forceClose = true;
        Close();
    }

    /// <summary>Удаление идёт после закрытия формы: при закрытии Shutdown ещё раз сохраняет настройки, и файлы создались бы заново.</summary>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        if (!resetRequested) return;
        string task = SystemTools.SetAutostart(false);
        string files = AppSettings.DeleteAll();
        if (task != null || files != null)
            MessageBox.Show("Не удалось удалить часть данных:\n" + task + (task != null && files != null ? "\n" : "") + files, AppSettings.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    string TypedPath() => txtPath.Text.Trim().Trim('"');

    /// <summary>Ручной ввод пути доступен только в продвинутом режиме; изменённый текст применяется кнопкой «Сохранить».</summary>
    void UpdatePathEditing()
    {
        if (txtPath == null || btnSavePath == null) return;
        bool adv = cfg.AdvancedMode;
        if (txtPath.ReadOnly == adv) txtPath.ReadOnly = !adv;
        if (!adv && txtPath.Text != cfg.ZapretPath) { txtPath.Text = cfg.ZapretPath; return; }
        bool dirty = adv && TypedPath() != cfg.ZapretPath;
        if (btnSavePath.Visible == dirty) return;
        btnSavePath.Visible = dirty;
        pathRow.ColumnStyles[2].Width = dirty ? Ui.S(140) : 0;
    }

    void ApplyPathFromText() => ApplyPath(TypedPath());

    void ApplyPath(string path)
    {
        if (path == cfg.ZapretPath) return;
        cfg.ZapretPath = path;
        cfg.Save();
        ReloadAll();
        UpdateUi();
        if (path.Length > 0 && !Strategy.IsValidZapretDir(path))
            AddLog("Папка выглядит не как Zapret (нет bin\\winws.exe или lists): " + path);
    }

    void OnAutoStartChanged()
    {
        chkTray.Enabled = chkAutoStart.Checked && cfg.Notifications != "offhide";
        if (loading) return;
        Cursor = Cursors.WaitCursor;
        try
        {
            string err = SystemTools.SetAutostart(chkAutoStart.Checked);
            if (err != null)
            {
                MessageBox.Show("Не удалось " + (chkAutoStart.Checked ? "создать" : "удалить") + " задачу автозапуска:\n" + err,
                    AppSettings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                loading = true;
                chkAutoStart.Checked = !chkAutoStart.Checked;
                chkTray.Enabled = chkAutoStart.Checked && cfg.Notifications != "offhide";
                loading = false;
                return;
            }
            cfg.AutoStartApp = chkAutoStart.Checked;
            cfg.Save();
            AddLog(cfg.AutoStartApp ? "Автозапуск включён (задача планировщика)." : "Автозапуск выключен (задача удалена).");
        }
        finally { Cursor = Cursors.Default; }
    }

    async Task UpdateZapretAsync()
    {
        bool install = !ZapretInstalled;
        string path = cfg.ZapretPath;
        string title = install ? "Установить Zapret" : "Обновить Zapret";

        if (install)
        {
            using var d = new FolderBrowserDialog
            {
                Description = "Папка для установки Zapret (пустая, без кириллицы и не в OneDrive)",
                UseDescriptionForTitle = true, ShowNewFolderButton = true, SelectedPath = path,
            };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            path = d.SelectedPath;
            if (!Updater.IsSafeTarget(path))
            {
                MessageBox.Show("Папка не пустая и в ней нет Zapret. Выберите пустую папку.", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        else
        {
            var ask = MessageBox.Show(
                "Будут скачаны последние файлы Zapret с GitHub и заменены папки bin, lists, utils и все .bat в:\n" + path +
                "\n\nВаши списки *-user.txt сохранятся, старые списки уйдут в бэкап. Версия не сверяется — обновление будет всегда. Продолжить?",
                title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ask != DialogResult.Yes) return;
        }

        if (AnyListDirty)
        {
            var r = MessageBox.Show("В списках есть несохранённые правки, при " + (install ? "установке" : "обновлении") + " они будут потеряны. Продолжить?", title,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
        }

        if (runner.IsRunning) StopBypass();
        updating = true;
        btnUpdate.Enabled = btnUpdateSettings.Enabled = false;
        Cursor = Cursors.AppStarting;
        try
        {
            string tag = await Updater.UpdateAsync(path, AddLog, CancellationToken.None);
            if (install) { cfg.ZapretPath = path; cfg.Save(); }
            AddLog("Zapret " + (install ? "установлен: " : "обновлён до ") + tag + ".");
            Notify("zapret-updated", false, install ? "Zapret установлен" : "Zapret обновлён", "Установлена версия " + tag + ".", ToolTipIcon.Info, () => ShowTab(PgLog));
            MessageBox.Show("Готово: " + tag, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AddLog((install ? "Установка" : "Обновление") + " не удалось: " + ex.Message);
            MessageBox.Show((install ? "Установка" : "Обновление") + " не удалось:\n" + ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            updating = false;
            Cursor = Cursors.Default;
            btnUpdate.Enabled = btnUpdateSettings.Enabled = !autoRunning;
            ReloadAll();
            ReloadListFiles(true);
            UpdateUi();
        }
    }
}
