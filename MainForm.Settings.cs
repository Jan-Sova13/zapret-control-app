namespace ZapretControl;

public partial class MainForm
{
    ScrollText txtPath;
    RoundButton btnBrowse;
    Check chkAutoStart, chkTray, chkStartBypass, chkTimestamps;
    Label lblVersion;

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
        var upd = Caption("Скачать или обновить сам zapret можно кнопкой «Обновить zapret» в левом нижнем углу окна.", Fonts.Small, Muted, 26);
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

        foreach (var chk in new[] { chkAutoStart, chkTray, chkStartBypass, chkTimestamps })
        {
            chk.Margin = new Padding(chk.Margin.Left, 0, 0, Ui.S(8));
            c.Controls.Add(chk);
        }

        var info = new Note
        {
            BackColor = Surface, ForeColor = Muted, Height = Ui.S(230), Margin = Ui.Pad(0, 12, 0, 0),
            Text = "Что программа делает в системе:\n" +
                   "• Хранит свои настройки (в том числе выбранную тему) в %AppData%\\Zapret Control App\\settings.json.\n" +
                   "• Пока обход включён: работает winws.exe и загружен драйвер WinDivert. При выключении, закрытии или падении программы winws завершается, драйвер выгружается.\n" +
                   "• TCP timestamps: если выключены, включаются на время работы и возвращаются обратно (если программа упадёт, вернутся при следующем запуске).\n" +
                   "• Автозапуск (только если галочка стоит): задача планировщика «Zapret Control App» — нужна, чтобы стартовать сразу с правами администратора. Снимете галочку — задача удалится.\n" +
                   "• Hosts, службы Windows и реестр не меняются.",
        };
        c.Controls.Add(info);

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
            ShowPage(3);
            MessageBox.Show("Укажите папку, куда положить zapret (можно пустую), на вкладке «Настройки» — и нажмите «Обновить zapret» ещё раз.", AppSettings.AppName);
            return;
        }

        var ask = MessageBox.Show(
            "Будут скачаны последние файлы zapret с GitHub и заменены папки bin, lists, utils и все .bat в:\n" + path +
            "\n\nВаши списки *-user.txt сохранятся, старые списки уйдут в бэкап. Версия не сверяется — обновление будет всегда. Продолжить?",
            "Обновить zapret", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (ask != DialogResult.Yes) return;

        if (AnyListDirty)
        {
            var r = MessageBox.Show("В списках есть несохранённые правки, при обновлении они будут потеряны. Продолжить?", "Обновить zapret",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
        }

        if (runner.IsRunning) StopBypass();
        btnUpdate.Enabled = false;
        Cursor = Cursors.AppStarting;
        try
        {
            string tag = await Updater.UpdateAsync(path, AddLog, CancellationToken.None);
            AddLog("zapret обновлён до " + tag + ".");
            MessageBox.Show("Готово: " + tag, "Обновить zapret", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AddLog("Обновление не удалось: " + ex.Message);
            MessageBox.Show("Обновление не удалось:\n" + ex.Message, "Обновить zapret", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            Cursor = Cursors.Default;
            btnUpdate.Enabled = true;
            ReloadAll();
            ReloadListFiles(true);
            UpdateUi();
        }
    }
}
