using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ZapretControlApp;

/// <summary>Переключатель-ползунок с подписью справа.</summary>
public class SwitchToggle : Control
{
    bool isChecked, hover;
    public event EventHandler CheckedChanged;

    public SwitchToggle()
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

    void Fit() => Width = Ui.S(52) + TextRenderer.MeasureText(string.IsNullOrEmpty(Text) ? " " : Text, Font).Width;

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

        int tw = Ui.S(40), th = Ui.S(22);
        var track = new Rectangle(Ui.S(1), (Height - th) / 2, tw, th);
        Color fill = isChecked ? p.AccentLight : p.Line;
        if (hover || Focused) fill = Ui.Shade(fill, 0.12f);
        if (!Enabled) fill = Ui.Mix(fill, back, 0.5f);
        using (var path = Ui.Round(track, th / 2))
        using (var br = new SolidBrush(fill))
            g.FillPath(br, path);

        int d = th - Ui.S(6);
        int kx = isChecked ? track.Right - d - Ui.S(3) : track.X + Ui.S(3);
        using (var br = new SolidBrush(isChecked ? Color.White : p.TextC))
            g.FillEllipse(br, kx, track.Y + Ui.S(3), d, d);

        TextRenderer.DrawText(g, Text, Font, new Rectangle(tw + Ui.S(12), 0, Width - tw - Ui.S(12), Height), Enabled ? p.TextC : p.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

/// <summary>Составная кнопка из двух отдельных секций: [значок + версия] и [«О программе»].</summary>
public class SplitButton : Control
{
    Image image;
    bool hi;
    int hot = -1, down = -1;

    public string LeftText = "", RightText = "О программе";

    public event EventHandler LeftClick, RightClick;

    public SplitButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Font = Fonts.Body;
        Height = Ui.S(34);
    }

    public Image Image { get => image; set { image = value; Invalidate(); } }
    public bool LeftHighlight { get => hi; set { hi = value; Invalidate(); } }

    int TextW(string t) => TextRenderer.MeasureText(string.IsNullOrEmpty(t) ? " " : t, Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

    Rectangle[] Segs()
    {
        int pad = Ui.S(14), gap = Ui.S(3);
        int w0 = pad * 2 + (image != null ? Ui.S(18) + Ui.S(8) : 0) + TextW(LeftText);
        int w1 = pad * 2 + TextW(RightText);
        return new[]
        {
            new Rectangle(0, 0, w0, Height - 1),
            new Rectangle(w0 + gap, 0, w1, Height - 1),
        };
    }

    public int NaturalWidth() => Segs()[1].Right + 1;
    public Rectangle LeftBounds => Segs()[0];

    int Hit(Point pt)
    {
        var s = Segs();
        for (int i = 0; i < s.Length; i++) if (s[i].Contains(pt)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int i = Hit(e.Location); if (i != hot) { hot = i; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { hot = -1; down = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); down = Hit(e.Location); Invalidate(); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int h = Hit(e.Location), d = down;
        down = -1;
        Invalidate();
        if (h < 0 || h != d) return;
        if (h == 0) LeftClick?.Invoke(this, EventArgs.Empty);
        else RightClick?.Invoke(this, EventArgs.Empty);
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
        var segs = Segs();
        for (int i = 0; i < segs.Length; i++)
        {
            bool accent = i == 0 && hi;
            Color fill = accent ? p.Danger : p.Line;
            if (i == hot && i == down) fill = Ui.Shade(fill, 0.22f);
            else if (i == hot) fill = Ui.Shade(fill, 0.12f);
            using (var path = Ui.Round(segs[i], Ui.R))
            using (var br = new SolidBrush(fill))
                g.FillPath(br, path);

            var r = segs[i];
            int x = r.X + Ui.S(14);
            if (i == 0 && image != null)
            {
                g.DrawImage(image, x, (Height - image.Height) / 2, image.Width, image.Height);
                x += Ui.S(18) + Ui.S(8);
            }
            string text = i == 0 ? LeftText : RightText;
            TextRenderer.DrawText(g, text, Font, new Rectangle(x, 0, r.Right - x - Ui.S(10), Height), accent ? Color.White : p.TextC,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }
}

/// <summary>Всплывающий список в стиле выпадающих списков программы (тот же SimpleList и ToolStripDropDown, что в Dropdown).</summary>
public static class PopupMenu
{
    public static void Show(Control anchor, IList<string> items, Action<int> pick, int x = 0, bool above = false, int width = 0)
    {
        var p = Theme.Current;
        var list = new SimpleList { Font = Fonts.Body, BackColor = p.Field, ForeColor = p.TextC };
        int w = width > 0 ? width : Math.Max(anchor.Width, Ui.S(280));
        list.Size = new Size(w, items.Count * list.RowHeight + Ui.S(6));
        list.SetItems(items);

        var host = new ToolStripControlHost(list) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = list.Size };
        var drop = new ToolStripDropDown { Padding = Padding.Empty, DropShadowEnabled = false, BackColor = p.Field };
        drop.Items.Add(host);
        list.ItemClicked += (s, e) =>
        {
            int i = list.SelectedIndex;
            drop.Close();
            if (i >= 0) anchor.BeginInvoke(new Action(() => pick(i)));
        };
        drop.Show(anchor, new Point(x, above ? -list.Height - Ui.S(2) : anchor.Height + Ui.S(2)));
    }
}

/// <summary>Блок текста на подложке, который сам подбирает высоту под ширину. Для вертикальных стопок.</summary>
public class FitNote : Note
{
    protected override void OnResize(EventArgs e) { base.OnResize(e); Fit(); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); }

    void Fit()
    {
        if (Width < Ui.S(60) || string.IsNullOrEmpty(Text)) return;
        int h = TextRenderer.MeasureText(Text, Font, new Size(Width - Ui.S(24), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top).Height + Ui.S(16);
        if (h != Height) Height = h;
    }
}

/// <summary>SimpleList с собственной подсказкой для каждой строки (у базового списка подсказка — текст строки).</summary>
public class TipList : SimpleList
{
    readonly ToolTip rowTip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 300, ReshowDelay = 100 };
    int lastTip = -2;

    public Func<int, string> TipFor;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        // смещение прокрутки у SimpleList закрыто, но равно значению его полосы прокрутки
        var bar = Controls.OfType<ThinScrollBar>().FirstOrDefault();
        int i = (e.Y + (bar?.Value ?? 0)) / Math.Max(1, RowHeight);
        if (i < 0 || i >= Count) i = -1;
        if (i == lastTip) return;
        lastTip = i;
        rowTip.SetToolTip(this, i >= 0 ? (TipFor?.Invoke(i) ?? "") : "");
    }

    protected override void OnMouseLeave(EventArgs e) { lastTip = -2; base.OnMouseLeave(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) rowTip.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Текст на скруглённой подложке: его можно выделять и копировать, ссылки открываются кликом. Прокрутка — общая ThinScrollBar.</summary>
public class LinkText : Control, IWheelTarget
{
    [DllImport("user32.dll")]
    static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    const int EM_LINESCROLL = 0x00B6;
    const int EM_GETFIRSTVISIBLELINE = 0x00CE;

    public readonly RichTextBox Box;
    readonly ThinScrollBar bar = new ThinScrollBar();
    readonly List<(int Start, int Length, string Url)> links = new List<(int, int, string)>();

    public event Action<string> LinkClicked;

    public LinkText()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Box = new RichTextBox
        {
            BorderStyle = BorderStyle.None, ReadOnly = true, ScrollBars = RichTextBoxScrollBars.None, DetectUrls = false,
            WordWrap = true, Font = Fonts.Body,
        };
        Controls.Add(Box);
        Controls.Add(bar);
        bar.Scroll += (s, e) =>
        {
            if (!Box.IsHandleCreated) return;
            int cur = SendMessage(Box.Handle, EM_GETFIRSTVISIBLELINE, 0, 0);
            if (bar.Value != cur) SendMessage(Box.Handle, EM_LINESCROLL, 0, bar.Value - cur);
        };
        Box.TextChanged += (s, e) => SyncBar();
        Box.SizeChanged += (s, e) => SyncBar();
        Box.HandleCreated += (s, e) => SyncBar();
        Box.VScroll += (s, e) => SyncBar();
        Box.KeyUp += (s, e) => SyncBar();
        Box.MouseMove += (s, e) => Box.Cursor = LinkAt(e.Location) != null ? Cursors.Hand : Cursors.IBeam;
        Box.MouseUp += (s, e) =>
        {
            if (e.Button != MouseButtons.Left || Box.SelectionLength > 0) return;
            string url = LinkAt(e.Location);
            if (url != null) LinkClicked?.Invoke(url);
        };
    }

    protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); Box.BackColor = BackColor; }
    protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); Box.ForeColor = ForeColor; }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

    /// <summary>Части текста: Url == null — обычный текст, иначе ссылка.</summary>
    public void SetContent(IEnumerable<(string Text, string Url)> parts, Color linkColor)
    {
        links.Clear();
        Box.Clear();
        using var underline = new Font(Box.Font, FontStyle.Underline);
        foreach (var (text, url) in parts)
        {
            int start = Box.TextLength;
            Box.SelectionStart = start;
            Box.SelectionLength = 0;
            Box.SelectionColor = url == null ? ForeColor : linkColor;
            Box.SelectionFont = url == null ? Box.Font : underline;
            Box.SelectedText = text;
            if (url != null) links.Add((start, text.Length, url));
        }
        Box.Select(0, 0);
        SyncBar();
    }

    string LinkAt(Point p)
    {
        if (links.Count == 0 || Box.TextLength == 0) return null;
        int i = Box.GetCharIndexFromPosition(p);
        foreach (var l in links)
        {
            if (i < l.Start || i >= l.Start + l.Length) continue;
            // GetCharIndexFromPosition возвращает ближайший символ даже справа от конца строки, поэтому проверяем границы символа
            var a = Box.GetPositionFromCharIndex(i);
            var b = Box.GetPositionFromCharIndex(i + 1);
            bool sameLine = Math.Abs(a.Y - b.Y) < Box.Font.Height / 2;
            if (Math.Abs(p.Y - a.Y - Box.Font.Height / 2) <= Box.Font.Height / 2 + 2 && p.X >= a.X && (!sameLine || p.X <= b.X)) return l.Url;
        }
        return null;
    }

    void Relayout()
    {
        Box.SetBounds(Ui.S(12), Ui.S(10), Math.Max(10, Width - Ui.S(12) - Ui.S(24)), Math.Max(10, Height - Ui.S(20)));
        bar.SetBounds(Width - Ui.S(14), Ui.S(6), Ui.S(8), Math.Max(10, Height - Ui.S(12)));
        SyncBar();
    }

    void SyncBar()
    {
        if (!Box.IsHandleCreated) return;
        int total = Box.GetLineFromCharIndex(Box.TextLength) + 1;
        int page = Math.Max(1, Box.ClientSize.Height / Math.Max(1, Box.Font.Height));
        bar.SetRange(total, page);
        bar.SetValueSilent(SendMessage(Box.Handle, EM_GETFIRSTVISIBLELINE, 0, 0));
    }

    public bool Wheel(int delta)
    {
        if (!Box.IsHandleCreated || bar.MaxValue <= 0) return false;
        int cur = SendMessage(Box.Handle, EM_GETFIRSTVISIBLELINE, 0, 0);
        int next = Math.Max(0, Math.Min(cur - delta * 3 / 120, bar.MaxValue));
        if (next == cur) return false;
        SendMessage(Box.Handle, EM_LINESCROLL, 0, next - cur);
        SyncBar();
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        try { PaintCore(e); }
        catch (Exception ex) { Diag.Write(GetType().Name + ".OnPaint", ex); }
    }

    void PaintCore(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.ParentBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), Ui.R);
        using (var br = new SolidBrush(BackColor)) g.FillPath(br, path);
        using (var pen = new Pen(Theme.Current.Line)) g.DrawPath(pen, path);
    }
}