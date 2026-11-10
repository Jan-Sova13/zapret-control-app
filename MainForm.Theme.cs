namespace ZapretControl;

public partial class MainForm
{
    Palette P => Theme.Current;
    Color Bg => P.Bg;
    Color Surface => P.Surface;
    Color Field => P.Field;
    Color Line => P.Line;
    Color Accent => P.Accent;
    Color AccentLight => P.AccentLight;
    Color TextC => P.TextC;
    Color Muted => P.Muted;
    Color Danger => P.Danger;
    Color DangerText => P.DangerText;
    Color WarnBg => P.WarnBg;
    Color WarnText => P.WarnText;
    Color WarnBtn => P.WarnBtn;

    /// <summary>Перекраска идёт заменой «цвет старой палитры → тот же цвет новой», поэтому цвета элементов всегда берутся только из палитры.</summary>
    static Color MapColor(Color c, Palette src, Palette dst)
    {
        if (c == src.Bg) return dst.Bg;
        if (c == src.Surface) return dst.Surface;
        if (c == src.Field) return dst.Field;
        if (c == src.Line) return dst.Line;
        if (c == src.Accent) return dst.Accent;
        if (c == src.AccentLight) return dst.AccentLight;
        if (c == src.TextC) return dst.TextC;
        if (c == src.Muted) return dst.Muted;
        if (c == src.Danger) return dst.Danger;
        if (c == src.DangerText) return dst.DangerText;
        if (c == src.WarnBg) return dst.WarnBg;
        if (c == src.WarnText) return dst.WarnText;
        if (c == src.WarnBtn) return dst.WarnBtn;
        return c;
    }

    static void Recolor(Control c, Palette src, Palette dst)
    {
        c.BackColor = MapColor(c.BackColor, src, dst);
        c.ForeColor = MapColor(c.ForeColor, src, dst);
        if (c is RoundButton b)
            b.ForeColor = (b.BackColor == dst.Accent || b.BackColor == dst.Danger) ? Color.White : dst.TextC;
        foreach (Control child in c.Controls) Recolor(child, src, dst);
    }

    static void InvalidateTree(Control c)
    {
        c.Invalidate();
        foreach (Control child in c.Controls) InvalidateTree(child);
    }

    void ApplyTheme(string name, bool save)
    {
        var src = Theme.Current;
        var dst = Theme.ByName(name);
        if (ReferenceEquals(src, dst)) return;

        SuspendLayout();
        Theme.Current = dst;
        Recolor(this, src, dst);
        RefreshThemeBits();
        UpdateUi();
        ResumeLayout(true);
        InvalidateTree(this);

        if (save) { cfg.Theme = name; cfg.Save(); }
    }

    /// <summary>Логотип GitHub и заголовок окна не перекрашиваются заменой цветов.</summary>
    void RefreshThemeBits()
    {
        if (btnGitHub != null)
        {
            var old = btnGitHub.Image;
            btnGitHub.Image = GitHubIcon.Make(TextC, Ui.S(18));
            old?.Dispose();
        }
        if (IsHandleCreated) Native.SetDarkTitleBar(Handle, P.Dark);
    }
}
