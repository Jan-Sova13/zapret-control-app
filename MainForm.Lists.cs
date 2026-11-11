using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapretControlApp;

public partial class MainForm
{
    class ListEditor
    {
        public string Path, Name;
        public ScrollText Box;
        public Label Title;
        public bool Dirty, ReadOnly, Loading;
    }

    readonly List<ListEditor> editors = new List<ListEditor>();
    ScrollHost listsHost;
    VStack stockBody;
    RoundButton btnStockToggle, btnSaveAll, btnSaveRestart, btnRevertAll;
    bool listsLoaded, stockBuilt;

    bool AnyListDirty => editors.Any(e => e.Dirty);

    Panel BuildPageLists()
    {
        var p = Page();
        var t = Grid(1, 2);
        Cols(t, Fill());
        Rows(t, Fill(), Abs(54));

        listsHost = new ScrollHost { Dock = DockStyle.Fill, BackColor = Bg, Margin = Padding.Empty };
        listsHost.Content.BackColor = Bg;
        t.Controls.Add(listsHost, 0, 0);

        var fl = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Bg, Padding = Ui.Pad(0, 10, 0, 0), WrapContents = false, Margin = Padding.Empty };
        btnSaveAll = Btn("Сохранить изменения", Accent, 200, 34);
        btnSaveRestart = Btn("Сохранить и перезапустить обход", Accent, 290, 34);
        btnRevertAll = Btn("Отменить правки", Line, 170, 34);
        btnSaveAll.Margin = btnSaveRestart.Margin = Ui.Pad(0, 0, 10, 0);
        btnSaveAll.Click += (s, e) => SaveAllLists();
        btnSaveRestart.Click += (s, e) => { if (SaveAllLists()) RestartIfRunning(); };
        btnRevertAll.Click += (s, e) => RevertAllLists();
        fl.Controls.AddRange(new Control[] { btnSaveAll, btnSaveRestart, btnRevertAll });
        t.Controls.Add(fl, 0, 1);

        p.Controls.Add(t);
        return p;
    }

    void SetListButtons(bool enabled)
    {
        btnSaveAll.Enabled = btnSaveRestart.Enabled = btnRevertAll.Enabled = enabled;
    }

    static string Describe(string n)
    {
        switch (n.ToLowerInvariant())
        {
            case "list-general-user.txt": return "★ Свой список: домены для обхода  (list-general-user.txt)";
            case "list-exclude-user.txt": return "★ Свой список: исключения доменов  (list-exclude-user.txt)";
            case "ipset-exclude-user.txt": return "★ Свой список: исключения IP и подсетей  (ipset-exclude-user.txt)";
            case "list-general.txt": return "Стоковый: домены для обхода  (list-general.txt)";
            case "list-exclude.txt": return "Стоковый: исключения доменов  (list-exclude.txt)";
            case "list-google.txt": return "Стоковый: Google / YouTube  (list-google.txt)";
            case "ipset-all.txt": return "Стоковый: IP и подсети для обхода  (ipset-all.txt)";
            case "ipset-exclude.txt": return "Стоковый: исключения IP  (ipset-exclude.txt)";
            default: return n;
        }
    }

    static bool IsIpList(string name) => name.StartsWith("ipset", StringComparison.OrdinalIgnoreCase);
    static bool IsExcludeList(string name) => name.Contains("exclude", StringComparison.OrdinalIgnoreCase);

    static string HintFor(string name)
    {
        bool user = name.EndsWith("-user.txt", StringComparison.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        if (IsIpList(name))
        {
            sb.AppendLine(IsExcludeList(name)
                ? "IP-адреса и подсети, которые НЕ нужно обрабатывать обходом."
                : "IP-адреса и подсети, к которым применяется обход.");
            sb.AppendLine("• По одной записи в строке: 1.2.3.4, подсеть 10.0.0.0/8, IPv6 — 2001:db8::/32.");
            sb.Append("• Без портов, схем и комментариев в конце строки. Строки с # в начале — комментарии.");
        }
        else
        {
            sb.AppendLine(IsExcludeList(name)
                ? "Домены, которые НЕ нужно обрабатывать обходом (исключения)."
                : "Домены, к которым применяется обход.");
            sb.AppendLine("• По одному домену в строке, только имя: example.com — без http://, без путей (/…), без пробелов и порта.");
            sb.AppendLine("• Поддомены учитываются автоматически: example.com покрывает и www.example.com, и cdn.example.com.");
            sb.AppendLine("• Маски со звёздочкой не поддерживаются. Регистр не важен. Кириллические домены — в punycode (xn--…).");
            sb.Append("• Строки с # в начале — комментарии. Файл не должен быть пустым (при пустом файле программа добавит заглушку).");
        }
        if (user) sb.Append("\n★ Это ваш список: он не затирается при обновлении zapret.");
        return sb.ToString();
    }

    void ReloadListFiles(bool force)
    {
        if (listsHost == null) return;
        if (AnyListDirty && !force) return;

        var host = listsHost.Content;
        host.SuspendLayout();
        editors.Clear();
        stockBuilt = false;
        stockBody = null;
        foreach (var c in host.Controls.Cast<Control>().ToList()) { host.Controls.Remove(c); c.Dispose(); }

        string dir = Path.Combine(cfg.ZapretPath ?? "", "lists");
        if (!Strategy.IsValidZapretDir(cfg.ZapretPath) || !Directory.Exists(dir))
        {
            var l = Caption("Папка zapret не выбрана. Укажите её на вкладке «Настройки» или скачайте zapret кнопкой «Обновить Zapret» внизу.", Fonts.Body, Muted, 30);
            host.Controls.Add(l);
            SetListButtons(false);
            host.ResumeLayout(true);
            listsLoaded = true;
            return;
        }

        SetListButtons(true);
        Strategy.EnsureUserLists(cfg.ZapretPath);
        var files = Directory.GetFiles(dir, "*.txt").ToList();
        bool IsUser(string f) => Path.GetFileName(f).EndsWith("-user.txt", StringComparison.OrdinalIgnoreCase);
        var user = files.Where(IsUser).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        string[] stockOrder = { "list-general.txt", "list-google.txt", "list-exclude.txt", "ipset-all.txt", "ipset-exclude.txt" };
        var stock = files.Where(f => !IsUser(f))
            .OrderBy(f => { int i = Array.IndexOf(stockOrder, Path.GetFileName(f).ToLowerInvariant()); return i < 0 ? 99 : i; })
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        host.Controls.Add(Caption("Свои списки", Fonts.Heading, TextC, 32));
        var sub = Caption("Сюда вносите свои домены и адреса. Эти файлы не затираются при обновлении zapret.", Fonts.Small, Muted, 24);
        sub.Margin = Ui.Pad(0, 0, 0, 8);
        host.Controls.Add(sub);
        foreach (var f in user) AddEditorCard(host, f);

        btnStockToggle = Btn("▶   Стоковые списки (нажмите, чтобы развернуть)", Line, 200, 40);
        btnStockToggle.TextAlign = ContentAlignment.MiddleLeft;
        btnStockToggle.Margin = Ui.Pad(0, 8, 0, 0);
        host.Controls.Add(btnStockToggle);

        var warn = new Note
        {
            Text = "Это стоковые файлы из релиза zapret. Лучше их не менять: при обновлении zapret они будут сброшены. " +
                   "Свои добавления вносите в свои списки (★) выше.",
            Font = Fonts.Body, BackColor = Bg, ForeColor = DangerText, Height = Ui.S(60), Margin = Ui.Pad(0, 0, 0, 4),
        };
        host.Controls.Add(warn);

        stockBody = new VStack { BackColor = Bg, Visible = false, Margin = Padding.Empty };
        host.Controls.Add(stockBody);

        btnStockToggle.Click += (s, e) =>
        {
            if (!stockBuilt)
            {
                stockBuilt = true;
                stockBody.SuspendLayout();
                foreach (var f in stock) AddEditorCard(stockBody, f);
                stockBody.Height = stockBody.ContentHeight();
                stockBody.ResumeLayout(true);
            }
            stockBody.Visible = !stockBody.Visible;
            btnStockToggle.Text = stockBody.Visible
                ? "▼   Стоковые списки (нажмите, чтобы свернуть)"
                : "▶   Стоковые списки (нажмите, чтобы развернуть)";
        };

        host.ResumeLayout(true);
        listsLoaded = true;
    }

    void AddEditorCard(VStack host, string path)
    {
        string name = Path.GetFileName(path);
        var ed = new ListEditor { Path = path, Name = name };
        editors.Add(ed);

        string hint = HintFor(name);
        var card = new VStack { BackColor = Bg, Margin = Ui.Pad(0, 0, 0, 18) };

        ed.Title = Caption(Describe(name), Fonts.Heading, TextC, 32);
        card.Controls.Add(ed.Title);

        var note = new Note { Text = hint, BackColor = Surface, ForeColor = Muted, Height = Ui.S(hint.Split('\n').Length * 19 + 20), Margin = Ui.Pad(0, 0, 0, 8) };
        card.Controls.Add(note);

        ed.Box = Input(true);
        ed.Box.Height = Ui.S(150);
        LoadEditor(ed);
        ed.Box.Box.TextChanged += (s, e) =>
        {
            if (ed.Loading || ed.ReadOnly) return;
            ed.Dirty = true;
            UpdateEditorTitle(ed);
        };
        card.Controls.Add(ed.Box);

        card.Height = card.ContentHeight();
        host.Controls.Add(card);
    }

    void UpdateEditorTitle(ListEditor ed)
        => ed.Title.Text = Describe(ed.Name) + (ed.Dirty ? "     ● не сохранено" : "");

    void LoadEditor(ListEditor ed)
    {
        ed.Loading = true;
        try
        {
            var fi = new FileInfo(ed.Path);
            if (fi.Length > 1_500_000)
            {
                ed.ReadOnly = true;
                ed.Box.ReadOnly = true;
                ed.Box.Text = "Файл слишком большой для редактирования здесь (" + (fi.Length / 1024) + " КБ). Откройте его в обычном текстовом редакторе.";
            }
            else
            {
                ed.ReadOnly = false;
                ed.Box.ReadOnly = false;
                ed.Box.Text = File.ReadAllText(ed.Path);
            }
            ed.Dirty = false;
            UpdateEditorTitle(ed);
        }
        catch (Exception ex) { AddLog("Не удалось прочитать список " + ed.Name + ": " + ex.Message); }
        finally { ed.Loading = false; }
    }

    void RevertAllLists()
    {
        if (AnyListDirty)
        {
            var r = MessageBox.Show("Отменить все несохранённые правки в списках?", AppSettings.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
        }
        foreach (var ed in editors) LoadEditor(ed);
    }

    static List<string> Validate(string name, string text)
    {
        var problems = new List<string>();
        bool ip = IsIpList(name);
        var domainRx = new Regex(@"^[a-z0-9_]([a-z0-9_-]{0,61}[a-z0-9_])?(\.[a-z0-9_]([a-z0-9_-]{0,61}[a-z0-9_])?)+$", RegexOptions.IgnoreCase);
        int n = 0;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            n++;
            string l = raw.Trim();
            if (l.Length == 0 || l.StartsWith("#")) continue;
            if (ip)
            {
                if (!IsIpOrCidr(l)) problems.Add("строка " + n + ": «" + Shorten(l) + "» — не похоже на IP или подсеть");
            }
            else if (!domainRx.IsMatch(l))
            {
                string why = l.Contains("://") ? "уберите http:// или https://"
                    : l.Contains('/') ? "уберите путь после домена"
                    : l.Contains('*') ? "маски (*) не поддерживаются"
                    : l.Contains(' ') ? "пробелы не допускаются"
                    : l.Contains(':') ? "уберите порт"
                    : "не похоже на имя домена";
                problems.Add("строка " + n + ": «" + Shorten(l) + "» — " + why);
            }
            if (problems.Count >= 8) break;
        }
        return problems;
    }

    static string Shorten(string s) => s.Length > 40 ? s.Substring(0, 40) + "…" : s;

    static bool IsIpOrCidr(string s)
    {
        var parts = s.Split('/');
        if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out var ip)) return false;
        if (parts.Length == 1) return true;
        if (!int.TryParse(parts[1], out int prefix)) return false;
        int max = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        return prefix >= 0 && prefix <= max;
    }

    /// <summary>Пустой список winws не принимает: добавляем заглушку, как это делает service.bat.</summary>
    static string EnsureNotEmpty(string name, string text)
    {
        bool hasData = text.Replace("\r", "").Split('\n').Any(l => l.Trim().Length > 0 && !l.Trim().StartsWith("#"));
        if (hasData) return text;
        string nl = text.EndsWith("\n") || text.Length == 0 ? "" : "\r\n";
        string filler = IsIpList(name) ? "203.0.113.113/32" : "domain.example.abc";
        return text + nl + filler + "\r\n";
    }

    bool SaveAllLists()
    {
        foreach (var ed in editors.Where(x => x.Dirty && !x.ReadOnly).ToList())
        {
            var problems = Validate(ed.Name, ed.Box.Text);
            if (problems.Count > 0)
            {
                var r = MessageBox.Show("В файле «" + ed.Name + "» найдены проблемы:\n\n" + string.Join("\n", problems) + "\n\nСохранить всё равно?",
                    "Проверка списка", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return false;
            }
            try
            {
                string text = EnsureNotEmpty(ed.Name, ed.Box.Text);
                File.WriteAllText(ed.Path, text, new UTF8Encoding(false));
                ed.Loading = true;
                ed.Box.Text = text;
                ed.Loading = false;
                ed.Dirty = false;
                UpdateEditorTitle(ed);
                AddLog("Сохранён список " + ed.Name + (runner.IsRunning ? " (чтобы применить, перезапустите обход)" : ""));
            }
            catch (Exception ex)
            {
                ed.Loading = false;
                MessageBox.Show("Не удалось сохранить " + ed.Name + ": " + ex.Message, AppSettings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
        return true;
    }
}
