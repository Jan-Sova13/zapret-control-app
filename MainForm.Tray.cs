using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ZapretControlApp;

public partial class MainForm
{
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr hIcon);

    NotifyIcon tray;
    ToolStripMenuItem miToggle, miStrategy;
    readonly Dictionary<string, Icon> trayIcons = new Dictionary<string, Icon>();
    readonly Dictionary<string, DateTime> notifyLast = new Dictionary<string, DateTime>();
    Bitmap logoBase;
    string trayKey, trayText;
    Action balloonAction;

    void BuildTray()
    {
        var menu = new ContextMenuStrip { Renderer = new ThemeRenderer(), Font = Fonts.Body };
        var miShow = new ToolStripMenuItem("Показать окно");
        miShow.Click += (s, e) => ShowMain();
        miToggle = new ToolStripMenuItem("Включить Zapret");
        miToggle.Click += (s, e) => TogglePower();
        miStrategy = new ToolStripMenuItem("Стратегия");
        miStrategy.DropDown.Renderer = new ThemeRenderer();
        miStrategy.DropDown.Font = Fonts.Body;
        var miCheck = new ToolStripMenuItem("Проверить сейчас");
        miCheck.Click += (s, e) => CheckNow();
        var miExit = new ToolStripMenuItem("Выход (всё выключится)");
        miExit.Click += (s, e) => Close();
        menu.Items.AddRange(new ToolStripItem[] { miShow, miToggle, miStrategy, miCheck, new ToolStripSeparator(), miExit });
        menu.Opening += (s, e) => RebuildStrategyMenu();

        tray = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = AppSettings.AppName,
            ContextMenuStrip = menu,
            Visible = cfg.Notifications != "offhide",
        };
        tray.MouseClick += (s, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (Visible && WindowState != FormWindowState.Minimized) HideToTray(); else ShowMain();
        };
        tray.DoubleClick += (s, e) => ShowMain();
        tray.BalloonTipClicked += (s, e) => balloonAction?.Invoke();
    }

    bool trayTipShown;

    /// <summary>У отключённой кнопки подсказка стандартным ToolTip не показывается, поэтому следим за мышью на родителе.</summary>
    void TrayHideTip(Point p)
    {
        bool over = !btnTrayHide.Enabled && btnTrayHide.Bounds.Contains(p);
        if (over && !trayTipShown)
        {
            trayTipShown = true;
            tip.Show("Включите значок в трее в настройках", headerInfo, btnTrayHide.Left, btnTrayHide.Bottom + Ui.S(4), 4000);
        }
        else if (!over && trayTipShown)
        {
            trayTipShown = false;
            tip.Hide(headerInfo);
        }
    }

    /// <summary>Режим «Выключены + убрать значок из трея»: значка нет, сворачивать некуда.</summary>
    void ApplyTrayMode()
    {
        bool on = cfg.Notifications != "offhide";
        if (tray != null) tray.Visible = on;
        btnTrayHide.Enabled = on;
        chkTray.Enabled = cfg.AutoStartApp && on;
    }

    void RebuildStrategyMenu()
    {
        miStrategy.DropDownItems.Clear();
        if (strategies.Count == 0)
        {
            miStrategy.DropDownItems.Add(new ToolStripMenuItem("Стратегий нет") { Enabled = false });
            return;
        }
        string cur = SelectedStrategy()?.Name;
        for (int i = 0; i < strategies.Count; i++)
        {
            int idx = i;
            var it = new ToolStripMenuItem((strategies[i].Name == cur ? "● " : "    ") + strategies[i].Name);
            it.Click += (s, e) =>
            {
                if (autoRunning || cbStrategy.SelectedIndex == idx) return;
                cbStrategy.SelectedIndex = idx;   // обработчик списка сохраняет выбор и перезапускает обход
            };
            miStrategy.DropDownItems.Add(it);
        }
    }

    void ShowTab(int page)
    {
        ShowMain();
        ShowPage(page);
    }

    bool WindowActive => Visible && WindowState != FormWindowState.Minimized && ReferenceEquals(Form.ActiveForm, this);

    /// <summary>Уведомление Windows. Не показывается при активном окне, одно и то же событие — не чаще раза в 10 минут.</summary>
    void Notify(string key, bool important, string title, string text, ToolTipIcon icon, Action onClick)
    {
        if (tray == null || shuttingDown || cfg.Notifications == "off" || cfg.Notifications == "offhide") return;
        if (cfg.Notifications == "important" && !important) return;
        if (WindowActive) return;
        var now = DateTime.Now;
        if (notifyLast.TryGetValue(key, out var last) && now - last < TimeSpan.FromMinutes(10)) return;
        notifyLast[key] = now;
        balloonAction = onClick;
        try { tray.ShowBalloonTip(8000, title, text, icon); } catch (Exception ex) { Diag.Write("notify", ex); }
    }

    void RefreshTray()
    {
        if (tray == null) return;
        string key = IsBusy ? "busy" : state switch
        {
            BypassState.Working => "ok",
            BypassState.Problems => "warn",
            BypassState.NoNetwork => "warn",
            BypassState.Crashed => "bad",
            _ => "off",
        };
        if (key != trayKey)
        {
            trayKey = key;
            tray.Icon = TrayIcon(key);
        }
        string tip = TrayTip();
        if (tip.Length > 63) tip = tip.Substring(0, 62) + "…";
        if (tip != trayText)
        {
            trayText = tip;
            tray.Text = tip;
        }
    }

    string TrayTip()
    {
        if (diagRunning) return AppSettings.AppName + " · идёт диагностика";
        if (autoRunning) return AppSettings.AppName + " · идёт автоподбор";
        string name = runner.CurrentStrategy;
        switch (state)
        {
            case BypassState.Working:
                return "Zapret включён · " + name + " · " + (lastCheckAt == DateTime.MinValue ? "проверка…" : "всё работает");
            case BypassState.Problems: return "Zapret включён · " + name + " · " + stateDetail;
            case BypassState.NoNetwork: return "Zapret включён · нет сети";
            case BypassState.Crashed: return "Zapret · сбой, подробности в журнале";
            default: return "Zapret выключен";
        }
    }

    Icon TrayIcon(string key)
    {
        if (trayIcons.TryGetValue(key, out var ic)) return ic;
        try
        {
            logoBase ??= LoadLogo();
            Color? tint = key switch
            {
                "off" => Color.FromArgb(110, 110, 110),
                "warn" => Color.FromArgb(224, 168, 40),
                "bad" => Color.FromArgb(200, 60, 52),
                "busy" => Color.FromArgb(60, 120, 200),
                _ => null,
            };
            using var src = tint == null ? new Bitmap(logoBase) : Tint(logoBase, tint.Value);
            ic = ToTrayIcon(src);
        }
        catch (Exception ex)
        {
            Diag.Write("tray-icon", ex);
            ic = Icon ?? SystemIcons.Application;
        }
        trayIcons[key] = ic;
        return ic;
    }

    Bitmap LoadLogo()
    {
        using var ico = !string.IsNullOrEmpty(Environment.ProcessPath) ? Icon.ExtractAssociatedIcon(Environment.ProcessPath) : null;
        return new Bitmap((ico ?? Icon ?? SystemIcons.Application).ToBitmap());
    }

    static float Lum(Color c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;

    /// <summary>Перекрашивает фон логотипа: яркость пикселя относительно фона даёт долю белого (сама буква остаётся белой, сглаживание сохраняется).</summary>
    static Bitmap Tint(Bitmap src, Color target)
    {
        int w = src.Width, h = src.Height;
        Color bg = Color.White;
        float bgLum = 255f;
        foreach (var pt in new[] { new Point(w / 8, h / 2), new Point(w - 1 - w / 8, h / 2), new Point(w / 2, h / 8), new Point(w / 2, h - 1 - h / 8) })
        {
            var c = src.GetPixel(pt.X, pt.Y);
            if (c.A >= 200 && Lum(c) < bgLum) { bg = c; bgLum = Lum(c); }
        }

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = src.GetPixel(x, y);
                if (c.A == 0) { bmp.SetPixel(x, y, c); continue; }
                float k = Math.Clamp((Lum(c) - bgLum) / Math.Max(1f, 255f - bgLum), 0f, 1f);
                bmp.SetPixel(x, y, Color.FromArgb(c.A,
                    (int)(target.R + (255 - target.R) * k),
                    (int)(target.G + (255 - target.G) * k),
                    (int)(target.B + (255 - target.B) * k)));
            }
        return bmp;
    }

    static Icon ToTrayIcon(Bitmap src)
    {
        var size = SystemInformation.SmallIconSize;
        using var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, 0, 0, size.Width, size.Height);
        }
        IntPtr h = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(h);
            return (Icon)tmp.Clone();
        }
        finally { DestroyIcon(h); }
    }
}
