namespace ZapretControl;

public partial class MainForm
{
    Color BtnFore(Color back) => back == Accent || back == Danger ? Color.White : TextC;

    /// <summary>Заливка кнопки: Accent — главное действие, Danger — опасное, Line — второстепенные.</summary>
    RoundButton Btn(string text, Color back, int w, int h)
        => new RoundButton { Text = text, Size = Ui.Sz(w, h), BackColor = back, ForeColor = BtnFore(back), Margin = Padding.Empty };

    Label Lbl(string text, Color color, Font font = null)
        => new Label { Text = text, AutoSize = true, ForeColor = color, BackColor = Color.Transparent, Font = font ?? Fonts.Body };

    /// <summary>Подпись фиксированной высоты для вертикальных стопок.</summary>
    Label Caption(string text, Font font, Color color, int height)
        => new Label
        {
            Text = text, AutoSize = false, Height = Ui.S(height), Font = font, ForeColor = color, BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty,
        };

    Check Chk(string text) => new Check { Text = text };

    Dropdown Drop(int w) => new Dropdown { Width = Ui.S(w), Margin = Padding.Empty };

    ScrollText Input(bool multi)
        => new ScrollText(multi) { BackColor = Field, ForeColor = TextC, Margin = Padding.Empty };

    Panel Page() => new Panel { Padding = Ui.Pad(Ui.Margin), BackColor = Bg };

    /// <summary>Сетка без автоподбора размеров: по умолчанию все ячейки делят место поровну, Cols/Rows задают свои размеры.</summary>
    TableLayoutPanel Grid(int cols, int rows)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = cols, RowCount = rows, BackColor = Bg, Margin = Padding.Empty };
        for (int i = 0; i < cols; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
        for (int i = 0; i < rows; i++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
        return t;
    }

    static void Cols(TableLayoutPanel t, params (SizeType Type, float Size)[] cols)
    {
        t.ColumnStyles.Clear();
        foreach (var c in cols) t.ColumnStyles.Add(new ColumnStyle(c.Type, c.Type == SizeType.Absolute ? Ui.S((int)c.Size) : c.Size));
    }

    static void Rows(TableLayoutPanel t, params (SizeType Type, float Size)[] rows)
    {
        t.RowStyles.Clear();
        foreach (var r in rows) t.RowStyles.Add(new RowStyle(r.Type, r.Type == SizeType.Absolute ? Ui.S((int)r.Size) : r.Size));
    }

    static (SizeType, float) Abs(int v) => (SizeType.Absolute, v);
    static (SizeType, float) Fill(int percent = 100) => (SizeType.Percent, percent);
}
