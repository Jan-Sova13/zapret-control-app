using System.Drawing.Drawing2D;

namespace ZapretControlApp;

public class Palette
{
    public bool Dark;
    public Color Bg, Surface, Field, Line, Accent, AccentLight, TextC, Muted, Danger, DangerText, WarnBg, WarnText, WarnBtn;
}

public static class Theme
{
    public static readonly Palette Dark = new Palette
    {
        Dark = true,
        Bg = Color.FromArgb(30, 30, 30), Surface = Color.FromArgb(42, 42, 42), Field = Color.FromArgb(24, 24, 24),
        Line = Color.FromArgb(64, 64, 64), Accent = Color.FromArgb(74, 82, 92), AccentLight = Color.FromArgb(110, 200, 140),
        TextC = Color.FromArgb(235, 235, 235), Muted = Color.FromArgb(150, 150, 150),
        Danger = Color.FromArgb(176, 70, 62), DangerText = Color.FromArgb(235, 110, 100),
        WarnBg = Color.FromArgb(110, 84, 28), WarnText = Color.FromArgb(255, 250, 235), WarnBtn = Color.FromArgb(150, 118, 45),
    };

    public static readonly Palette Light = new Palette
    {
        Dark = false,
        Bg = Color.FromArgb(245, 245, 245), Surface = Color.FromArgb(232, 232, 232), Field = Color.FromArgb(255, 255, 255),
        Line = Color.FromArgb(205, 205, 205), Accent = Color.FromArgb(66, 74, 84), AccentLight = Color.FromArgb(30, 120, 70),
        TextC = Color.FromArgb(30, 30, 30), Muted = Color.FromArgb(100, 100, 100),
        Danger = Color.FromArgb(190, 60, 52), DangerText = Color.FromArgb(180, 35, 35),
        WarnBg = Color.FromArgb(255, 236, 179), WarnText = Color.FromArgb(92, 64, 0), WarnBtn = Color.FromArgb(240, 210, 120),
    };

    public static readonly Palette Green = new Palette
    {
        Dark = true,
        Bg = Color.FromArgb(28, 34, 31), Surface = Color.FromArgb(38, 46, 42), Field = Color.FromArgb(22, 27, 25),
        Line = Color.FromArgb(58, 72, 64), Accent = Color.FromArgb(63, 107, 85), AccentLight = Color.FromArgb(120, 190, 150),
        TextC = Color.FromArgb(232, 238, 234), Muted = Color.FromArgb(150, 165, 156),
        Danger = Color.FromArgb(170, 74, 66), DangerText = Color.FromArgb(235, 120, 110),
        WarnBg = Color.FromArgb(110, 84, 28), WarnText = Color.FromArgb(255, 250, 235), WarnBtn = Color.FromArgb(150, 118, 45),
    };

    public static Palette Current = Dark;

    public static Palette ByName(string name) => name == "light" ? Light : (name == "green" ? Green : Dark);
}

/// <summary>Единственные допустимые сочетания шрифт+размер. Новые не добавлять (см. STYLE_GUIDE.md).</summary>
public static class Fonts
{
    public static readonly Font Body = new Font("Segoe UI", 10f);
    public static readonly Font Small = new Font("Segoe UI", 9f);
    public static readonly Font Heading = new Font("Segoe UI", 11f, FontStyle.Bold);
    public static readonly Font Mono = new Font("Consolas", 10f);
    public static readonly Font Display = new Font("Segoe UI", 14f, FontStyle.Bold);
}

public static class Ui
{
    public const int Margin = 16;

    /// <summary>Масштаб системного DPI. Все размеры в коде задаются в пикселях при 96 DPI и проходят через S().</summary>
    public static readonly float K;

    static Ui()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        K = g.DpiX / 96f;
    }

    public static int S(int v) => (int)Math.Round(v * K);
    public static int R => S(6);

    public static Padding Pad(int all) => new Padding(S(all));
    public static Padding Pad(int h, int v) => new Padding(S(h), S(v), S(h), S(v));
    public static Padding Pad(int l, int t, int r, int b) => new Padding(S(l), S(t), S(r), S(b));
    public static Size Sz(int w, int h) => new Size(S(w), S(h));

    public static Color Mix(Color a, Color b, float t)
        => Color.FromArgb(255, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    /// <summary>Светлее для тёмного цвета, темнее для светлого.</summary>
    public static Color Shade(Color c, float t) => Mix(c, c.GetBrightness() < 0.5f ? Color.White : Color.Black, t);

    public static Color ParentBack(Control c)
    {
        Color back = c.Parent != null ? c.Parent.BackColor : Theme.Current.Bg;
        return back.A < 255 ? Theme.Current.Bg : back;
    }

    public static GraphicsPath Round(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static GraphicsPath RoundTop(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        p.CloseFigure();
        return p;
    }
}
