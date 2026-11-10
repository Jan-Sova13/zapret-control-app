using System.Drawing.Drawing2D;

namespace ZapretControl;

public class RoundButton : Button
{
    bool hover, down;

    public RoundButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Font = Fonts.Body;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Color back = Ui.ParentBack(this);
        g.Clear(back);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color fill = BackColor, fore = ForeColor;
        if (!Enabled) { fill = Ui.Mix(fill, back, 0.55f); fore = Ui.Mix(fore, fill, 0.45f); }
        else if (down) fill = Ui.Shade(fill, 0.22f);
        else if (hover) fill = Ui.Shade(fill, 0.12f);

        using (var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), Ui.R))
        using (var br = new SolidBrush(fill))
            g.FillPath(br, path);

        var flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        if (Image != null)
        {
            var ts = TextRenderer.MeasureText(g, Text, Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int gap = Ui.S(8);
            int x = Padding.Left + (Width - Padding.Horizontal - (Image.Width + gap + ts.Width)) / 2;
            g.DrawImage(Image, x, (Height - Image.Height) / 2, Image.Width, Image.Height);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + Image.Width + gap, 0, ts.Width + Ui.S(4), Height), fore,
                flags | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            return;
        }

        switch (TextAlign)
        {
            case ContentAlignment.TopLeft:
            case ContentAlignment.MiddleLeft:
            case ContentAlignment.BottomLeft: flags |= TextFormatFlags.Left; break;
            case ContentAlignment.TopRight:
            case ContentAlignment.MiddleRight:
            case ContentAlignment.BottomRight: flags |= TextFormatFlags.Right; break;
            default: flags |= TextFormatFlags.HorizontalCenter; break;
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(Padding.Left + Ui.S(8), 0, Width - Padding.Horizontal - Ui.S(16), Height), fore, flags);
    }
}

public class Check : Control
{
    bool isChecked, hover;
    public event EventHandler CheckedChanged;

    public Check()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = Fonts.Body;
        Height = Ui.S(28);
    }

    public bool Checked
    {
        get => isChecked;
        set
        {
            if (isChecked == value) return;
            isChecked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    void Fit() => Width = Ui.S(30) + TextRenderer.MeasureText(string.IsNullOrEmpty(Text) ? " " : Text, Font).Width;

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); Invalidate(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); if (Enabled) Checked = !Checked; }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; } base.OnKeyDown(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        Color back = Ui.ParentBack(this);
        g.Clear(back);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int bs = Ui.S(18);
        var box = new Rectangle(Ui.S(1), (Height - bs) / 2, bs, bs);
        Color fill = isChecked ? p.AccentLight : p.Field;
        Color border = isChecked ? p.AccentLight : (hover || Focused ? Ui.Mix(p.Line, p.TextC, 0.45f) : p.Line);
        if (!Enabled) { fill = Ui.Mix(fill, back, 0.5f); border = Ui.Mix(border, back, 0.5f); }

        using (var path = Ui.Round(box, Ui.S(4)))
        {
            using (var br = new SolidBrush(fill)) g.FillPath(br, path);
            using (var pen = new Pen(border)) g.DrawPath(pen, path);
        }
        if (isChecked)
        {
            using var pen = new Pen(Color.White, Math.Max(2f, Ui.S(2))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[]
            {
                new Point(box.X + Ui.S(4), box.Y + bs / 2),
                new Point(box.X + Ui.S(7), box.Y + bs / 2 + Ui.S(3)),
                new Point(box.X + Ui.S(13), box.Y + Ui.S(5)),
            });
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(bs + Ui.S(12), 0, Width - bs - Ui.S(12), Height), Enabled ? p.TextC : p.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

/// <summary>Текстовый блок на скруглённой подложке (BackColor — подложка, ForeColor — текст).</summary>
public class Note : Control
{
    public Note()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = Fonts.Small;
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.ParentBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), Ui.R))
        using (var br = new SolidBrush(BackColor))
            g.FillPath(br, path);
        var r = new Rectangle(Ui.S(12), Ui.S(8), Width - Ui.S(24), Height - Ui.S(16));
        TextRenderer.DrawText(g, Text, Font, r, ForeColor, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top);
    }
}

/// <summary>Тонкий индикатор: BackColor — дорожка, ForeColor — заполнение.</summary>
public class ThinProgress : Control
{
    int max = 100, val;

    public ThinProgress()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public int Maximum { get => max; set { max = Math.Max(1, value); Invalidate(); } }
    public int Value { get => val; set { val = Math.Max(0, Math.Min(value, max)); Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.ParentBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Ui.Round(track, Height / 2))
        using (var br = new SolidBrush(BackColor))
            g.FillPath(br, path);
        int w = (int)((long)(Width - 1) * val / max);
        if (w < Height) return;
        using (var path = Ui.Round(new Rectangle(0, 0, w, Height - 1), Height / 2))
        using (var br = new SolidBrush(ForeColor))
            g.FillPath(br, path);
    }
}

public class LinePanel : Panel
{
    public LinePanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Current.Line);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}
