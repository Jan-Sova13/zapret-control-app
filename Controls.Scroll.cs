using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ZapretControlApp;

public interface IWheelTarget
{
    /// <summary>true — колесо обработано; false — прокручивать нечего, событие уйдёт родителю.</summary>
    bool Wheel(int delta);
}

/// <summary>Колесо мыши всегда уходит элементу под курсором, а не тому, у кого фокус.</summary>
public class WheelFilter : IMessageFilter
{
    const int WM_MOUSEWHEEL = 0x020A;

    [DllImport("user32.dll")]
    static extern IntPtr WindowFromPoint(Point p);

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WM_MOUSEWHEEL) return false;
        long w = m.WParam.ToInt64(), l = m.LParam.ToInt64();
        int delta = (short)((w >> 16) & 0xFFFF);
        var pt = new Point((short)(l & 0xFFFF), (short)((l >> 16) & 0xFFFF));
        Control c = Control.FromHandle(WindowFromPoint(pt));
        while (c != null)
        {
            if (c is IWheelTarget t && t.Wheel(delta)) return true;
            c = c.Parent;
        }
        return false;
    }
}

public class ThinScrollBar : Control
{
    int total, page = 1, value, dragOffset;
    bool dragging, hover;

    public event EventHandler Scroll;

    public ThinScrollBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public int Value => value;
    public int MaxValue => Math.Max(0, total - page);

    public void SetRange(int totalLength, int pageLength)
    {
        total = Math.Max(0, totalLength);
        page = Math.Max(1, pageLength);
        value = Math.Min(value, MaxValue);
        Invalidate();
    }

    public void SetValueSilent(int v)
    {
        value = Math.Max(0, Math.Min(v, MaxValue));
        Invalidate();
    }

    public void SetValue(int v)
    {
        v = Math.Max(0, Math.Min(v, MaxValue));
        if (v == value) return;
        value = v;
        Invalidate();
        Scroll?.Invoke(this, EventArgs.Empty);
    }

    Rectangle Thumb()
    {
        int inset = Ui.S(2);
        int track = Math.Max(1, Height - inset * 2);
        int len = (int)Math.Min(track, Math.Max(Ui.S(28), (long)track * page / Math.Max(total, 1)));
        int y = inset + (MaxValue > 0 ? (int)((long)(track - len) * value / MaxValue) : 0);
        return new Rectangle(Ui.S(1), y, Math.Max(2, Width - Ui.S(2)), len);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        try { PaintCore(e); }
        catch (Exception ex) { Diag.Write(GetType().Name + ".OnPaint", ex); }
    }

    void PaintCore(PaintEventArgs e)
    {
        var g = e.Graphics;
        Color back = Ui.ParentBack(this);
        g.Clear(back);
        if (MaxValue <= 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color thumb = Ui.Mix(back, Theme.Current.TextC, dragging ? 0.45f : (hover ? 0.38f : 0.28f));
        var r = Thumb();
        using (var path = Ui.Round(r, r.Width / 2))
        using (var br = new SolidBrush(thumb))
            g.FillPath(br, path);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (MaxValue <= 0) return;
        var t = Thumb();
        if (t.Contains(e.Location)) { dragging = true; dragOffset = e.Y - t.Y; }
        else SetValue(value + (e.Y < t.Y ? -page : page));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        int inset = Ui.S(2);
        int range = Math.Max(1, Height - inset * 2 - Thumb().Height);
        SetValue((int)((long)(e.Y - dragOffset - inset) * MaxValue / range));
    }

    protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Invalidate(); base.OnMouseUp(e); }
}

/// <summary>Вертикальная стопка: дети выстраиваются друг под другом на всю ширину, высота стопки подстраивается.</summary>
public class VStack : Panel
{
    bool busy;

    public VStack() { DoubleBuffered = true; }

    /// <summary>Высота стопки по текущим высотам детей. Детям нужно задавать Height заранее: родитель читает их до собственной раскладки.</summary>
    public int ContentHeight()
    {
        int y = Padding.Top;
        foreach (Control c in Controls)
            if (c.Visible) y += c.Margin.Top + c.Height + c.Margin.Bottom;
        return y + Padding.Bottom;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (busy) return;
        busy = true;
        try
        {
            int y = Padding.Top;
            int w = ClientSize.Width - Padding.Horizontal;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                y += c.Margin.Top;
                c.SetBounds(Padding.Left + c.Margin.Left, y, Math.Max(1, w - c.Margin.Horizontal), c.Height);
                y += c.Height + c.Margin.Bottom;
            }
            y += Padding.Bottom;
            if (Height != y) Height = y;
        }
        finally { busy = false; }
    }
}

public class ScrollHost : Panel, IWheelTarget
{
    public readonly VStack Content = new VStack();
    readonly ThinScrollBar bar = new ThinScrollBar();
    int offset;

    public ScrollHost()
    {
        DoubleBuffered = true;
        Controls.Add(bar);
        Controls.Add(Content);
        Content.SizeChanged += (s, e) => UpdateRange();
        bar.Scroll += (s, e) => { offset = bar.Value; Content.Top = -offset; };
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Content.SetBounds(0, -offset, Math.Max(10, ClientSize.Width - Ui.S(14)), Content.Height);
        bar.SetBounds(ClientSize.Width - Ui.S(10), Ui.S(2), Ui.S(8), Math.Max(10, ClientSize.Height - Ui.S(4)));
        UpdateRange();
    }

    void UpdateRange()
    {
        bar.SetRange(Content.Height, ClientSize.Height);
        offset = Math.Max(0, Math.Min(offset, bar.MaxValue));
        Content.Top = -offset;
        bar.SetValueSilent(offset);
    }

    public bool Wheel(int delta)
    {
        if (bar.MaxValue <= 0) return false;
        int next = Math.Max(0, Math.Min(offset - delta * Ui.S(60) / 120, bar.MaxValue));
        if (next == offset) return false;
        offset = next;
        Content.Top = -offset;
        bar.SetValueSilent(offset);
        return true;
    }
}

/// <summary>Поле ввода на скруглённой подложке. У многострочного своя полоса прокрутки вместо системной.</summary>
public class ScrollText : Control, IWheelTarget
{
    [DllImport("user32.dll")]
    static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    const int EM_LINESCROLL = 0x00B6;
    const int EM_GETFIRSTVISIBLELINE = 0x00CE;

    public readonly TextBox Box;
    readonly ThinScrollBar bar = new ThinScrollBar();
    readonly bool multi;

    public ScrollText(bool multiline)
    {
        multi = multiline;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Box = new TextBox
        {
            BorderStyle = BorderStyle.None, Multiline = multiline, ScrollBars = ScrollBars.None,
            WordWrap = !multiline, AcceptsReturn = multiline, Font = multiline ? Fonts.Mono : Fonts.Body,
        };
        Controls.Add(Box);
        if (multi)
        {
            Controls.Add(bar);
            bar.Scroll += (s, e) =>
            {
                if (!Box.IsHandleCreated) return;
                int cur = SendMessage(Box.Handle, EM_GETFIRSTVISIBLELINE, 0, 0);
                if (bar.Value != cur) SendMessage(Box.Handle, EM_LINESCROLL, 0, bar.Value - cur);
            };
        }
        Box.TextChanged += (s, e) => { OnTextChanged(e); SyncBar(); };
        Box.KeyUp += (s, e) => SyncBar();
        Box.MouseUp += (s, e) => SyncBar();
        Box.SizeChanged += (s, e) => SyncBar();
        Box.HandleCreated += (s, e) => SyncBar();
        Box.Enter += (s, e) => Invalidate();
        Box.Leave += (s, e) => Invalidate();
        Height = Ui.S(34);
    }

    public override string Text { get => Box.Text; set => Box.Text = value; }
    public bool ReadOnly { get => Box.ReadOnly; set => Box.ReadOnly = value; }
    public bool WordWrap { get => Box.WordWrap; set { Box.WordWrap = value; SyncBar(); } }
    public Font TextFont { get => Box.Font; set { Box.Font = value; Relayout(); } }
    public int TextLength => Box.TextLength;
    public void AppendText(string s) => Box.AppendText(s);

    protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); Box.BackColor = BackColor; }
    protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); Box.ForeColor = ForeColor; }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

    void Relayout()
    {
        int left = Ui.S(10);
        if (multi)
        {
            Box.SetBounds(left, Ui.S(8), Math.Max(10, Width - left - Ui.S(22)), Math.Max(10, Height - Ui.S(16)));
            bar.SetBounds(Width - Ui.S(14), Ui.S(6), Ui.S(8), Math.Max(10, Height - Ui.S(12)));
        }
        else
        {
            Box.SetBounds(left, Math.Max(0, (Height - Box.Height) / 2), Math.Max(10, Width - left * 2), Box.Height);
        }
        SyncBar();
    }

    void SyncBar()
    {
        if (!multi || !Box.IsHandleCreated) return;
        int total = Box.GetLineFromCharIndex(Box.TextLength) + 1;
        int page = Math.Max(1, Box.ClientSize.Height / Math.Max(1, Box.Font.Height));
        bar.SetRange(total, page);
        bar.SetValueSilent(SendMessage(Box.Handle, EM_GETFIRSTVISIBLELINE, 0, 0));
    }

    public bool Wheel(int delta)
    {
        if (!multi || !Box.IsHandleCreated || bar.MaxValue <= 0) return false;
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
        var p = Theme.Current;
        g.Clear(Ui.ParentBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), Ui.R);
        using (var br = new SolidBrush(BackColor)) g.FillPath(br, path);
        using (var pen = new Pen(Box.Focused ? p.AccentLight : p.Line)) g.DrawPath(pen, path);
    }
}

public class SimpleList : Control, IWheelTarget
{
    readonly List<string> items = new List<string>();
    readonly ThinScrollBar bar = new ThinScrollBar();
    readonly ToolTip tip = new ToolTip();
    int sel = -1, hot = -1, offset;

    public bool ShowTips;
    public int RowHeight { get; set; } = Ui.S(28);

    public event EventHandler SelectedIndexChanged;
    public event EventHandler ItemClicked;
    public event EventHandler ItemActivated;

    public SimpleList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Font = Fonts.Body;
        Controls.Add(bar);
        bar.Scroll += (s, e) => { offset = bar.Value; Invalidate(); };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tip.Dispose();
        base.Dispose(disposing);
    }

    public int Count => items.Count;

    public int SelectedIndex
    {
        get => sel;
        set
        {
            if (value < -1 || value >= items.Count) value = -1;
            if (value == sel) return;
            sel = value;
            if (sel >= 0) EnsureVisible(sel);
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetItems(IEnumerable<string> list)
    {
        items.Clear();
        items.AddRange(list);
        if (sel >= items.Count) sel = -1;
        UpdateScroll();
        Invalidate();
    }

    void UpdateScroll()
    {
        bar.SetRange(items.Count * RowHeight, Height);
        offset = Math.Max(0, Math.Min(offset, bar.MaxValue));
        bar.SetValueSilent(offset);
    }

    public void EnsureVisible(int i)
    {
        int top = i * RowHeight;
        if (top < offset) offset = top;
        else if (top + RowHeight > offset + Height) offset = top + RowHeight - Height;
        offset = Math.Max(0, Math.Min(offset, bar.MaxValue));
        bar.SetValueSilent(offset);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        bar.SetBounds(Width - Ui.S(12), Ui.S(4), Ui.S(8), Math.Max(10, Height - Ui.S(8)));
        UpdateScroll();
    }

    int Hit(int y)
    {
        int i = (y + offset) / RowHeight;
        return i >= 0 && i < items.Count ? i : -1;
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
        using var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), Ui.R);
        using (var br = new SolidBrush(p.Field)) g.FillPath(br, path);

        g.SetClip(path);
        int right = Width - Ui.S(18);
        for (int i = offset / RowHeight; i < items.Count; i++)
        {
            int y = i * RowHeight - offset;
            if (y >= Height) break;
            var row = new Rectangle(Ui.S(4), y + Ui.S(1), right - Ui.S(4), RowHeight - Ui.S(2));
            Color fore = p.TextC;
            if (i == sel)
            {
                using var rp = Ui.Round(row, Ui.S(4));
                using var rb = new SolidBrush(p.Accent);
                g.FillPath(rb, rp);
                fore = Color.White;
            }
            else if (i == hot)
            {
                using var rp = Ui.Round(row, Ui.S(4));
                using var rb = new SolidBrush(Ui.Mix(p.Field, p.TextC, 0.10f));
                g.FillPath(rb, rp);
            }
            TextRenderer.DrawText(g, items[i], Font, new Rectangle(row.X + Ui.S(8), row.Y, row.Width - Ui.S(12), row.Height), fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
        g.ResetClip();
        using (var pen = new Pen(p.Line)) g.DrawPath(pen, path);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = Hit(e.Y);
        if (i == hot) return;
        hot = i;
        if (ShowTips) tip.SetToolTip(this, i >= 0 ? items[i] : "");
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        int i = Hit(e.Y);
        if (i >= 0) SelectedIndex = i;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (Hit(e.Y) >= 0) ItemClicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (Hit(e.Y) >= 0) ItemActivated?.Invoke(this, EventArgs.Empty);
    }

    protected override bool IsInputKey(Keys keyData)
        => keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Down && sel < items.Count - 1) SelectedIndex = sel + 1;
        else if (e.KeyCode == Keys.Up && sel > 0) SelectedIndex = sel - 1;
        else if (e.KeyCode == Keys.Enter && sel >= 0) ItemActivated?.Invoke(this, EventArgs.Empty);
    }

    public bool Wheel(int delta)
    {
        if (bar.MaxValue <= 0) return false;
        int next = Math.Max(0, Math.Min(offset - delta * RowHeight * 3 / 120, bar.MaxValue));
        if (next == offset) return false;
        offset = next;
        bar.SetValueSilent(offset);
        Invalidate();
        return true;
    }
}
