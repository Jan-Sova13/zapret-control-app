namespace ZapretControlApp;

public partial class MainForm
{
    ScrollText txtPath;
    RoundButton btnBrowse;
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

        c.Controls.Add(Caption("Папка zapret (где лежат bin, lists, utils и general*.bat):", Fonts.Body, Muted, 28));

        var row = Grid(2, 1);
        row.Dock = DockStyle.None;
        row.Height = Ui.S(40);
        row.Margin = Ui.Pad(0, 0, 0, 6);
        Cols(row, Fill(), Abs(140));
        txtPath = Input(false);
        txtPath.Dock = DockStyle.Fill;
        txtPath.Margin = Ui.Pad(0, 0, 10, 0);
        txtPath.Text = cfg.ZapretPath;
        txtPath.Box.Leave += (s, e) => ApplyPathFromText();
        txtPath.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { ApplyPathFromText(); e.SuppressKeyPress = true; } };
        btnBrowse = Btn("Выбрать…", Line, 130, 34);
        btnBrowse.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        btnBrowse.Click += (s, e) =>
        {
            using var d = new FolderBrowserDialog { Description = "Папка с zapret-discord-youtube", UseDescriptionForTitle = true, SelectedPath = cfg.ZapretPath };
            if (d.ShowDialog(this) == DialogResult.OK) { txtPath.Text = d.SelectedPath; ApplyPathFromText(); }
        };
        row.Controls.Add(txtPath, 0, 0);
        row.Controls.Add(btnBrowse, 1, 0);
        c.Controls.Add(row);

        lblVersion = Caption("", Fonts.Body, Muted, 28);
        c.Controls.Add(lblVersion);
        var upd = Caption("Скачать или обновить сам zapret можно кнопкой «Обновить Zapret» вверху окна.", Fonts.Small, Muted, 26);
        upd.Margin = Ui.Pad(0, 0, 0, 14);
        c.Controls.Add(upd);

        chkAutoStart = Chk("Запускать программу при входе в Windows");
        chkAutoStart.Checked = cfg.AutoStartApp;
        chkAutoStart.CheckedChanged += (s, e) => OnAutoStartChanged();

        chkTray = Chk("При автозапуске сворачиваться в трей");
        chkTray.Margin = Ui.Pad(30, 0, 0, 0);
        chkTray.Checked = cfg.StartInTray;
        chkTray.Enabled = cfg.AutoStartApp;
        chkTray.CheckedChanged += (s, e) => { if (!loading) { cfg.StartInTray = chkTray.Checked; cfg.Save(); } };

        chkStartBypass = Chk("Включать обход сразу при запуске программы");
        chkStartBypass.Checked = cfg.StartBypassOnLaunch;
        chkStartBypass.CheckedChanged += (s, e) => { if (!loading) { cfg.StartBypassOnLaunch = chkStartBypass.Checked; cfg.Save(); } };

        chkTimestamps = Chk("Включать TCP timestamps на время работы (при выключении возвращаются)");
        chkTimestamps.Checked = cfg.ManageTimestamps;
        chkTimestamps.CheckedChanged += (s, e) => { if (!loading) { cfg.ManageTimestamps = chkTimestamps.Checked; cfg.Save(); } };

        chkAutoUpdate = Chk("Автоматически проверять обновления программы (при запуске и раз в сутки)");
        chkAutoUpdate.Checked = cfg.AutoCheckUpdates;
        chkAutoUpdate.CheckedChanged += (s, e) => { if (!loading) { cfg.AutoCheckUpdates = chkAutoUpdate.Checked; cfg.Save(); } };

        foreach (var chk in new[] { chkAutoStart, chkTray, chkStartBypass, chkTimestamps, chkAutoUpdate })
        {
            chk.Margin = new Padding(chk.Margin.Left, 0, 0, Ui.S(8));
            c.Controls.Add(chk);
        }
        advSettings.Add(chkTimestamps);
        advSettings.Add(chkAutoUpdate);

        cbNotify = Drop(220);
        cbNotify.Items.Add(new Opt { Text = "Выкл", Value = "off" });
        cbNotify.Items.Add(new Opt { Text = "Только важные", Value = "important" });
        cbNotify.Items.Add(new Opt { Text = "Все", Value = "all" });
        cbNotify.SelectedIndex = Math.Max(0, cbNotify.Items.Cast<Opt>().ToList().FindIndex(o => o.Value == cfg.Notifications));
        cbNotify.SelectedIndexChanged += (s, e) =>
        {
            if (loading || !(cbNotify.SelectedItem is Opt o)) return;
            cfg.Notifications = o.Value;
            cfg.Save();
        };
        tip.SetToolTip(cbNotify, "Всплывающие уведомления Windows. «Только важные» — Zapret упал, проблемы с сервисами.\nНе показываются, пока окно программы активно.");
        c.Controls.Add(OptionRow("Уведомления:", cbNotify));

        c.ResumeLayout(true);
        p.Controls.Add(host);
        return p;
    }

    void ApplyPathFromText()
    {
        if (txtPath == null) return;
        string path = txtPath.Text.Trim().Trim('"');
        if (path == cfg.ZapretPath) return;
        cfg.ZapretPath = path;
        cfg.Save();
        ReloadAll();
        UpdateUi();
        if (path.Length > 0 && !Strategy.IsValidZapretDir(path))
            AddLog("Папка выглядит не как zapret (нет bin\\winws.exe или lists): " + path);
    }

    void OnAutoStartChanged()
    {
        chkTray.Enabled = chkAutoStart.Checked;
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
                chkTray.Enabled = chkAutoStart.Checked;
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
        ApplyPathFromText();
        string path = cfg.ZapretPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            ShowPage(PgSettings);
            MessageBox.Show("Укажите папку, куда положить zapret (можно пустую), на вкладке «Настройки» — и нажмите «Обновить Zapret» ещё раз.", AppSettings.AppName);
            return;
        }

        var ask = MessageBox.Show(
            "Будут скачаны последние файлы zapret с GitHub и заменены папки bin, lists, utils и все .bat в:\n" + path +
            "\n\nВаши списки *-user.txt сохранятся, старые списки уйдут в бэкап. Версия не сверяется — обновление будет всегда. Продолжить?",
            "Обновить Zapret", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (ask != DialogResult.Yes) return;

        if (AnyListDirty)
        {
            var r = MessageBox.Show("В списках есть несохранённые правки, при обновлении они будут потеряны. Продолжить?", "Обновить Zapret",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
        }

        if (runner.IsRunning) StopBypass();
        updating = true;
        btnUpdate.Enabled = false;
        Cursor = Cursors.AppStarting;
        try
        {
            string tag = await Updater.UpdateAsync(path, AddLog, CancellationToken.None);
            AddLog("zapret обновлён до " + tag + ".");
            Notify("zapret-updated", false, "Zapret обновлён", "Установлена версия " + tag + ".", ToolTipIcon.Info, () => ShowTab(PgLog));
            MessageBox.Show("Готово: " + tag, "Обновить Zapret", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AddLog("Обновление не удалось: " + ex.Message);
            MessageBox.Show("Обновление не удалось:\n" + ex.Message, "Обновить Zapret", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            updating = false;
            Cursor = Cursors.Default;
            btnUpdate.Enabled = !autoRunning;
            ReloadAll();
            ReloadListFiles(true);
            UpdateUi();
        }
    }
}
