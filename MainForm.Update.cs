using System.Diagnostics;

namespace ZapretControlApp;

public partial class MainForm
{
    ReleaseInfo availableUpdate;
    System.Windows.Forms.Timer updateTimer;
    bool updateDialogOpen;

    void InitUpdates()
    {
        _ = Task.Run(AppUpdate.CleanupTemp);
        updateTimer = new System.Windows.Forms.Timer { Interval = 24 * 60 * 60 * 1000 };
        updateTimer.Tick += (s, e) => _ = BackgroundUpdateCheckAsync();
        updateTimer.Start();
        _ = BackgroundUpdateCheckAsync();
    }

    /// <summary>Тихая проверка: без релизов, без сети и при ошибках ничего не показывает.</summary>
    async Task BackgroundUpdateCheckAsync()
    {
        if (!cfg.AutoCheckUpdates || shuttingDown) return;
        var r = await AppUpdate.CheckAsync(CancellationToken.None);
        if (shuttingDown || IsDisposed || r.Status != UpdateStatus.Available) return;
        availableUpdate = r.Info;
        if (r.Info.Tag == cfg.SkippedVersion) return;
        ShowNewVersion();
        if (cfg.NotifiedVersion != r.Info.Tag)
        {
            cfg.NotifiedVersion = r.Info.Tag;
            cfg.Save();
            Notify("app-update", false, "Вышла новая версия программы", r.Info.Tag + " — нажмите, чтобы посмотреть.", ToolTipIcon.Info, () => { ShowMain(); ShowUpdateWindow(r.Info); });
        }
    }

    void ShowNewVersion()
    {
        if (shuttingDown || IsDisposed) return;
        footerGrid.ColumnStyles[2].Width = Ui.S(300);
        lblNewVersion.Visible = true;
        btnGitHub.MidHighlight = true;
    }

    void HideNewVersion()
    {
        footerGrid.ColumnStyles[2].Width = 0;
        lblNewVersion.Visible = false;
        btnGitHub.MidHighlight = false;
    }

    async Task ManualUpdateCheckAsync()
    {
        Cursor = Cursors.AppStarting;
        UpdateCheck r;
        try { r = await AppUpdate.CheckAsync(CancellationToken.None); }
        finally { Cursor = Cursors.Default; }

        switch (r.Status)
        {
            case UpdateStatus.Failed:
                MessageBox.Show("Не удалось связаться с GitHub.", AppSettings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                break;
            case UpdateStatus.UpToDate:
                MessageBox.Show("У вас последняя версия (" + AppUpdate.Format(AppUpdate.Current) + ").", AppSettings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
            default:
                availableUpdate = r.Info;
                ShowNewVersion();
                ShowUpdateWindow(r.Info);
                break;
        }
    }

    void ShowVersionMenu()
    {
        var items = new[] { "Что нового", "Проверить обновления", "Сообщить о проблеме", "Открыть папку настроек и журналов" };
        PopupMenu.Show(btnGitHub, items, i =>
        {
            switch (i)
            {
                case 0: OpenUrl(AppUpdate.RepoUrl + "/releases/tag/" + AppUpdate.Format(AppUpdate.Current)); break;
                case 1: _ = ManualUpdateCheckAsync(); break;
                case 2: OpenUrl(AppUpdate.RepoUrl + "/issues"); break;
                default:
                    try
                    {
                        Directory.CreateDirectory(AppSettings.Dir);
                        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + AppSettings.Dir + "\"") { UseShellExecute = true });
                    }
                    catch (Exception ex) { AddLog("Не удалось открыть папку: " + ex.Message); }
                    break;
            }
        }, btnGitHub.MidBounds.X, true, Ui.S(340));
    }

    Form Dlg(string title, int w, int h)
    {
        var f = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent, AutoScaleMode = AutoScaleMode.None, ClientSize = Ui.Sz(w, h),
            BackColor = Bg, ForeColor = TextC, Font = Fonts.Body,
        };
        f.HandleCreated += (s, e) => Native.SetDarkTitleBar(f.Handle, P.Dark);
        return f;
    }

    FlowLayoutPanel ButtonRow()
        => new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Bg, Padding = Ui.Pad(0, 10, 0, 0), WrapContents = false, Margin = Padding.Empty };

    void ShowAbout()
    {
        using var f = Dlg("О программе", 760, 600);
        var root = Grid(1, 3);
        root.Padding = Ui.Pad(Ui.Margin);
        Rows(root, Abs(34), Fill(), Abs(54));

        root.Controls.Add(Caption(AppSettings.AppName + "  " + AppUpdate.Format(AppUpdate.Current), Fonts.Heading, TextC, 34), 0, 0);

        var host = new ScrollHost { Dock = DockStyle.Fill, BackColor = Bg, Margin = Padding.Empty };
        host.Content.BackColor = Bg;
        host.Content.Controls.Add(new FitNote
        {
            BackColor = Surface, ForeColor = TextC, Font = Fonts.Body, Height = Ui.S(400),
            Text =
                "Лицензия: GNU GPL-3.0.\n" +
                "Оболочка для Zapret (Flowseal/zapret-discord-youtube): запускает winws.exe, подбирает стратегии, следит за работой обхода. " +
                "Сам Zapret — отдельный проект, скачивается кнопкой «Обновить Zapret».\n\n" +
                "Что программа делает в системе:\n" +
                "• Хранит настройки (в том числе тему и режим интерфейса), журнал сбоев crash.log, бэкапы списков и временные файлы обновления в %AppData%\\Zapret Control App.\n" +
                "• Пока обход включён: работает winws.exe и загружен драйвер WinDivert. При выключении, закрытии или падении программы winws завершается, драйвер выгружается.\n" +
                "• TCP timestamps: если выключены, включаются на время работы и возвращаются обратно (если программа упадёт, вернутся при следующем запуске). Можно отключить в настройках (продвинутый режим).\n" +
                "• Автозапуск (только если стоит галочка): задача планировщика «Zapret Control App» — нужна, чтобы стартовать сразу с правами администратора. Снимете галочку — задача удалится.\n" +
                "• Сторожевой режим (включён по умолчанию): пока обход включён, раз в несколько минут открывает 2–4 сайта выбранных сервисов; после сна, смены сети или падения winws.exe перезапускает обход. Настройки системы при этом не меняются.\n" +
                "• Значок в трее и уведомления Windows показывают состояние и важные события.\n" +
                "• Диагностика только читает состояние системы (процессы, службы, реестр, DNS, время) и ничего не меняет.\n" +
                "• Сеть: запросы к сайтам для проверок; к api.github.com и github.com — для проверки и загрузки обновлений программы (при запуске и раз в сутки, можно отключить) и Zapret (только по кнопке «Обновить Zapret»).\n" +
                "• Обновление программы: по кнопке «Установить и перезапустить» новый exe скачивается в %AppData%\\Zapret Control App\\update, проверяется контрольная сумма, после выхода из программы текущий exe заменяется.\n" +
                "• Hosts, настройки DNS, службы Windows и реестр не меняются (кроме временной службы драйвера WinDivert и задачи автозапуска).",
        });
        root.Controls.Add(host, 0, 1);

        var row = ButtonRow();
        var bRepo = Btn("Репозиторий программы", Line, 220, 34);
        var bZapret = Btn("Zapret (Flowseal)", Line, 190, 34);
        var bClose = Btn("Закрыть", Accent, 120, 34);
        bRepo.Margin = bZapret.Margin = Ui.Pad(0, 0, 10, 0);
        bRepo.Click += (s, e) => OpenUrl(AppUpdate.RepoUrl);
        bZapret.Click += (s, e) => OpenUrl("https://github.com/Flowseal/zapret-discord-youtube");
        bClose.Click += (s, e) => f.Close();
        row.Controls.AddRange(new Control[] { bRepo, bZapret, bClose });
        root.Controls.Add(row, 0, 2);

        f.Controls.Add(root);
        f.ShowDialog(this);
    }

    void ShowUpdateWindow(ReleaseInfo info)
    {
        if (info == null || updateDialogOpen) return;
        updateDialogOpen = true;
        bool install = false;
        CancellationTokenSource dl = null;
        try
        {
            using var f = Dlg("Обновление программы", 860, 560);
            bool noSum = info.AssetUrl != null && info.Sha256 == null;
            var root = Grid(1, 5);
            root.Padding = Ui.Pad(Ui.Margin);
            Rows(root, Abs(36), Fill(), Abs(noSum || info.AssetUrl == null ? 64 : 0), Abs(30), Abs(54));

            root.Controls.Add(Caption("Доступна версия " + info.Tag + " (сейчас " + AppUpdate.Format(AppUpdate.Current) + ")", Fonts.Heading, TextC, 36), 0, 0);

            var host = new ScrollHost { Dock = DockStyle.Fill, BackColor = Bg, Margin = Padding.Empty };
            host.Content.BackColor = Bg;
            host.Content.Controls.Add(new FitNote { BackColor = Surface, ForeColor = TextC, Font = Fonts.Body, Height = Ui.S(60), Text = AppUpdate.PlainNotes(info.Notes) });
            root.Controls.Add(host, 0, 1);

            if (noSum || info.AssetUrl == null)
            {
                var warn = new FitNote
                {
                    BackColor = Bg, ForeColor = DangerText, Font = Fonts.Body, Dock = DockStyle.Fill, Margin = Ui.Pad(0, 8, 0, 0),
                    Text = info.AssetUrl == null
                        ? "В релизе нет файла, который можно установить автоматически. Скачайте программу вручную."
                        : "В релизе нет контрольной суммы: проверить целостность файла не получится. Устанавливайте, только если доверяете источнику.",
                };
                root.Controls.Add(warn, 0, 2);
            }

            var pg = Grid(2, 1);
            Cols(pg, Abs(420), Fill());
            var bar = new ThinProgress { Dock = DockStyle.Fill, Margin = Ui.Pad(0, 9, 12, 9), BackColor = Surface, ForeColor = AccentLight, Visible = false };
            var lbl = Caption("", Fonts.Body, Muted, 30);
            lbl.Dock = DockStyle.Fill;
            pg.Controls.Add(bar, 0, 0);
            pg.Controls.Add(lbl, 1, 0);
            root.Controls.Add(pg, 0, 3);

            var row = ButtonRow();
            var bInstall = Btn("Установить и перезапустить", Accent, 250, 34);
            var bManual = Btn("Скачать вручную", Line, 170, 34);
            var bSkip = Btn("Пропустить эту версию", Line, 210, 34);
            var bLater = Btn("Позже", Line, 100, 34);
            var bCancel = Btn("Отмена", Danger, 110, 34);
            bInstall.Margin = bManual.Margin = bSkip.Margin = bLater.Margin = bCancel.Margin = Ui.Pad(0, 0, 10, 0);
            bInstall.Enabled = info.AssetUrl != null;
            bCancel.Visible = false;
            row.Controls.AddRange(new Control[] { bInstall, bManual, bSkip, bLater, bCancel });
            root.Controls.Add(row, 0, 4);

            bManual.Click += (s, e) => OpenUrl(info.PageUrl);
            bLater.Click += (s, e) => f.Close();
            bSkip.Click += (s, e) =>
            {
                cfg.SkippedVersion = info.Tag;
                cfg.Save();
                HideNewVersion();
                f.Close();
            };
            bCancel.Click += (s, e) => dl?.Cancel();
            f.FormClosing += (s, e) => dl?.Cancel();

            bInstall.Click += async (s, e) =>
            {
                if (autoRunning || updating || restarting || diagRunning)
                {
                    MessageBox.Show("Сначала дождитесь окончания автоподбора, диагностики или обновления.", AppSettings.AppName);
                    return;
                }
                if (AnyListDirty &&
                    MessageBox.Show("В списках есть несохранённые правки, при установке они будут потеряны. Продолжить?", "Обновление", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;

                bInstall.Enabled = bManual.Enabled = bSkip.Enabled = bLater.Enabled = false;
                bCancel.Visible = true;
                bar.Visible = true;
                bar.Value = 0;
                lbl.ForeColor = Muted;
                lbl.Text = "Загрузка…";
                updating = true;
                dl = new CancellationTokenSource();
                string exe = null;
                try
                {
                    var progress = new Progress<(long Done, long Total)>(p =>
                    {
                        if (p.Total > 0) { bar.Maximum = (int)Math.Max(1, p.Total / 1024); bar.Value = (int)(p.Done / 1024); }
                        lbl.Text = "Загружено " + (p.Done / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + " МБ";
                    });
                    exe = await AppUpdate.DownloadAsync(info, progress, dl.Token);
                }
                catch (OperationCanceledException) { lbl.Text = "Загрузка отменена."; }
                catch (Exception ex)
                {
                    AddLog("Обновление программы: " + ex.Message);
                    lbl.ForeColor = DangerText;
                    lbl.Text = "Не удалось: " + ex.Message;
                    MessageBox.Show(ex.Message + "\n\nСтарая версия осталась рабочей. Можно скачать обновление вручную:\n" + info.PageUrl,
                        "Обновление", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    updating = false;
                    dl.Dispose();
                    dl = null;
                }

                if (exe != null)
                {
                    string err = AppUpdate.LaunchInstaller(exe, runner.IsRunning);
                    if (err == null) { install = true; f.Close(); return; }
                    AddLog("Обновление программы: не удалось запустить установщик: " + err);
                    MessageBox.Show("Не удалось запустить установку: " + err + "\n\nСкачайте обновление вручную:\n" + info.PageUrl, "Обновление", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                bar.Visible = false;
                bCancel.Visible = false;
                bInstall.Enabled = info.AssetUrl != null;
                bManual.Enabled = bSkip.Enabled = bLater.Enabled = true;
            };

            f.Controls.Add(root);
            f.ShowDialog(this);
        }
        finally { updateDialogOpen = false; }

        if (install)
        {
            AddLog("Установка обновления: программа закрывается, обход будет возвращён после запуска новой версии.");
            forceClose = true;
            Close();
        }
    }
}
