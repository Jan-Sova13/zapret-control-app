using System.Drawing.Drawing2D;

namespace ZapretControlApp;

/// <summary>Выпадающий список: поле с рамкой в цвете темы и всплывающий список с той же полосой прокрутки, что и везде.</summary>
public class Dropdown : Control
{
    public readonly List<object> Items = new List<object>();
    int sel = -1;
    bool hover, open;
    DateTime closedAt = DateTime.MinValue;

    public event EventHandler SelectedIndexChanged;

    public Dropdown()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = Fonts.Body;
        Height = Ui.S(34);
    }

    public int SelectedIndex
    {
        get => sel >= 0 && sel < Items.Count ? sel : -1;
        set
        {
            if (value < -1 || value >= Items.Count) value = -1;
            bool changed = value != sel;
            sel = value;
            Invalidate();
            if (changed) SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public object SelectedItem => SelectedIndex >= 0 ? Items[sel] : null;

    /// <summary>Вызывать после прямых изменений Items.</summary>
    public void ItemsChanged()
    {
        if (sel >= Items.Count) sel = -1;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if ((DateTime.UtcNow - closedAt).TotalMilliseconds < 250) return;
        Open();
    }

    protected override bool IsInputKey(Keys keyData)
        => keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Down && sel < Items.Count - 1) SelectedIndex = sel + 1;
        else if (e.KeyCode == Keys.Up && sel > 0) SelectedIndex = sel - 1;
        else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) Open();
    }

    void Open()
    {
        if (!Enabled || Items.Count == 0 || open) return;
        var p = Theme.Current;
        var list = new SimpleList { Font = Font, BackColor = p.Field, ForeColor = p.TextC };
        int rows = Math.Min(Items.Count, 10);
        list.Size = new Size(Width, rows * list.RowHeight + Ui.S(6));
        list.SetItems(Items.Select(i => i.ToString()));
        list.SelectedIndex = SelectedIndex;

        var host = new ToolStripControlHost(list) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = list.Size };
        var drop = new ToolStripDropDown { Padding = Padding.Empty, DropShadowEnabled = false, BackColor = p.Field };
        drop.Items.Add(host);
        list.ItemClicked += (s, e) => { SelectedIndex = list.SelectedIndex; drop.Close(); };
        drop.Closed += (s, e) => { open = false; closedAt = DateTime.UtcNow; Invalidate(); };

        open = true;
        Invalidate();
        drop.Show(this, new Point(0, Height + Ui.S(2)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        try { PaintCore(e); }
        catch (Exception ex) { Diag.Write(GetType().Name + ".OnPaint", ex); }
    }

    void PaintCore(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        Color back = Ui.ParentBack(this);
        g.Clear(back);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color border = open || Focused ? p.AccentLight : (hover ? Ui.Mix(p.Line, p.TextC, 0.3f) : p.Line);
        Color fill = Enabled ? p.Field : Ui.Mix(p.Field, back, 0.5f);
        using (var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), Ui.R))
        {
            using (var br = new SolidBrush(fill)) g.FillPath(br, path);
            using (var pen = new Pen(border)) g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(g, SelectedItem?.ToString() ?? "", Font, new Rectangle(Ui.S(12), 0, Width - Ui.S(40), Height),
            Enabled ? p.TextC : p.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        int cx = Width - Ui.S(18), cy = Height / 2;
        using var chevron = new Pen(Enabled ? p.Muted : p.Line, Math.Max(1.5f, Ui.S(2))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(chevron, new[] { new Point(cx - Ui.S(4), cy - Ui.S(2)), new Point(cx, cy + Ui.S(2)), new Point(cx + Ui.S(4), cy - Ui.S(2)) });
    }
}

public class TabStrip : Control
{
    public readonly List<string> Tabs = new List<string>();
    int selected, hot = -1;

    public event EventHandler SelectedChanged;

    public TabStrip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = Fonts.Body;
        Height = Ui.S(42);
    }

    public int Selected
    {
        get => selected;
        set
        {
            if (value == selected || value < 0 || value >= Tabs.Count) return;
            selected = value;
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    List<Rectangle> Rects()
    {
        var res = new List<Rectangle>();
        int x = Ui.S(Ui.Margin);
        foreach (var t in Tabs)
        {
            int w = TextRenderer.MeasureText(t, Font).Width + Ui.S(36);
            res.Add(new Rectangle(x, Ui.S(6), w, Height - Ui.S(6)));
            x += w + Ui.S(4);
        }
        return res;
    }

    int Hit(Point pt)
    {
        var rects = Rects();
        for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(pt)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = Hit(e.Location);
        if (i == hot) return;
        hot = i;
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int i = Hit(e.Location);
        if (i >= 0) Selected = i;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        try { PaintCore(e); }
        catch (Exception ex) { Diag.Write(GetType().Name + ".OnPaint", ex); }
    }

    void PaintCore(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        g.Clear(Ui.ParentBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var line = new Pen(p.Line)) g.DrawLine(line, 0, Height - 1, Width, Height - 1);

        var rects = Rects();
        for (int i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            bool active = i == selected;
            if (active)
            {
                using (var path = Ui.RoundTop(new Rectangle(r.X, r.Y, r.Width, r.Height - 1), Ui.R))
                using (var br = new SolidBrush(p.Surface))
                    g.FillPath(br, path);
                using (var br = new SolidBrush(p.AccentLight))
                    g.FillRectangle(br, r.X, Height - Ui.S(3), r.Width, Ui.S(3));
            }
            else if (i == hot)
            {
                using (var path = Ui.RoundTop(new Rectangle(r.X, r.Y, r.Width, r.Height - 1), Ui.R))
                using (var br = new SolidBrush(Ui.Mix(p.Bg, p.Surface, 0.5f)))
                    g.FillPath(br, path);
            }
            TextRenderer.DrawText(g, Tabs[i], Font, new Rectangle(r.X, r.Y, r.Width, r.Height - Ui.S(3)),
                active || i == hot ? p.TextC : p.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}

public class ThemeRenderer : ToolStripProfessionalRenderer
{
    class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Current.Field;
        public override Color ImageMarginGradientBegin => Theme.Current.Field;
        public override Color ImageMarginGradientMiddle => Theme.Current.Field;
        public override Color ImageMarginGradientEnd => Theme.Current.Field;
        public override Color MenuItemSelected => Theme.Current.Accent;
        public override Color MenuItemSelectedGradientBegin => Theme.Current.Accent;
        public override Color MenuItemSelectedGradientEnd => Theme.Current.Accent;
        public override Color MenuItemBorder => Theme.Current.Accent;
        public override Color MenuBorder => Theme.Current.Line;
        public override Color SeparatorDark => Theme.Current.Line;
        public override Color SeparatorLight => Theme.Current.Line;
    }

    public ThemeRenderer() : base(new Colors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Selected ? Color.White : Theme.Current.TextC;
        base.OnRenderItemText(e);
    }
}
