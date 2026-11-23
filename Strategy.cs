using System.Text;
using System.Text.RegularExpressions;

namespace ZapretControlApp;

/// <summary>Стратегия = bat-файл из папки zapret. Аргументы winws.exe берём прямо из него.</summary>
public class Strategy
{
    public string Name;      // имя без .bat
    public string FilePath;  // полный путь к bat
    public string RawArgs;   // всё, что идёт после winws.exe (с переменными %BIN% и т.п.)

    public override string ToString() => Name;

    static readonly Regex VarRx = new Regex("%[A-Za-z_~][A-Za-z0-9_~]*%", RegexOptions.Compiled);

    public static List<Strategy> LoadAll(string zapretDir)
    {
        var list = new List<Strategy>();
        if (string.IsNullOrWhiteSpace(zapretDir) || !Directory.Exists(zapretDir)) return list;

        foreach (var f in Directory.GetFiles(zapretDir, "*.bat"))
        {
            string fn = Path.GetFileName(f);
            if (fn.StartsWith("service", StringComparison.OrdinalIgnoreCase)) continue;
            var st = Parse(f);
            if (st != null) list.Add(st);
        }
        list.Sort((a, b) => NaturalCompare(a.Name, b.Name));
        return list;
    }

    static Strategy Parse(string file)
    {
        string text;
        try { text = File.ReadAllText(file, Encoding.UTF8); } catch { return null; }

        // Склеиваем строки, оканчивающиеся на ^ (продолжение в bat)
        var logical = new List<string>();
        var cur = new StringBuilder();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            string l = raw.TrimEnd();
            if (l.EndsWith("^"))
            {
                cur.Append(l.Substring(0, l.Length - 1)).Append(' ');
            }
            else
            {
                cur.Append(l);
                logical.Add(cur.ToString());
                cur.Clear();
            }
        }
        if (cur.Length > 0) logical.Add(cur.ToString());

        string line = null;
        foreach (var l in logical)
        {
            string t = l.TrimStart();
            if (t.StartsWith("::") || t.StartsWith("rem ", StringComparison.OrdinalIgnoreCase)) continue;
            if (l.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase) >= 0) { line = l; break; }
        }
        if (line == null) return null;

        int idx = line.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase) + "winws.exe".Length;
        string args = line.Substring(idx).TrimStart();
        if (args.StartsWith("\"")) args = args.Substring(1);
        args = Regex.Replace(args, @"\^(.)", "$1");   // ^! -> !, ^^ -> ^ и т.д.
        args = Regex.Replace(args, @"\s+", " ").Trim();
        if (!args.Contains("--")) return null;

        return new Strategy { Name = Path.GetFileNameWithoutExtension(file), FilePath = file, RawArgs = args };
    }

    /// <summary>Подставляет переменные, как это делает bat. Возвращает готовую командную строку.</summary>
    public string BuildArgs(string zapretDir, string gameMode, out string problem)
    {
        string bin = Path.Combine(zapretDir, "bin") + "\\";
        string lists = Path.Combine(zapretDir, "lists") + "\\";

        // как в service.bat: при выключенном фильтре порт-заглушка 12
        string tcp = "12", udp = "12", any = "12";
        const string range = "1024-65535";
        switch (gameMode)
        {
            case "all": tcp = range; udp = range; any = range; break;
            case "tcp": tcp = range; any = range; break;
            case "udp": udp = range; any = range; break;
        }

        string a = RawArgs;
        a = a.Replace("%BIN%", bin, StringComparison.OrdinalIgnoreCase);
        a = a.Replace("%LISTS%", lists, StringComparison.OrdinalIgnoreCase);
        a = a.Replace("%GameFilterTCP%", tcp, StringComparison.OrdinalIgnoreCase);
        a = a.Replace("%GameFilterUDP%", udp, StringComparison.OrdinalIgnoreCase);
        a = a.Replace("%GameFilter%", any, StringComparison.OrdinalIgnoreCase);

        var left = VarRx.Matches(a).Select(m => m.Value).Distinct().ToList();
        problem = left.Count > 0
            ? "В стратегии «" + Name + "» остались неподдерживаемые переменные: " + string.Join(", ", left)
            : null;
        return a;
    }

    /// <summary>Создаёт пользовательские списки, если их нет (так же, как service.bat) — иначе winws не стартует.</summary>
    public static void EnsureUserLists(string zapretDir)
    {
        try
        {
            string lists = Path.Combine(zapretDir, "lists");
            Directory.CreateDirectory(lists);
            var utf8 = new UTF8Encoding(false);

            string p1 = Path.Combine(lists, "ipset-exclude-user.txt");
            if (!File.Exists(p1)) File.WriteAllText(p1, "203.0.113.113/32\r\n", utf8);

            string p2 = Path.Combine(lists, "list-general-user.txt");
            if (!File.Exists(p2)) File.WriteAllText(p2, "# Never leave this file empty\r\ndomain.example.abc\r\n", utf8);

            string p3 = Path.Combine(lists, "list-exclude-user.txt");
            if (!File.Exists(p3)) File.WriteAllText(p3, "domain.example.abc\r\n", utf8);
        }
        catch { }
    }

    public static bool IsValidZapretDir(string dir)
        => !string.IsNullOrWhiteSpace(dir)
           && File.Exists(Path.Combine(dir, "bin", "winws.exe"))
           && Directory.Exists(Path.Combine(dir, "lists"));

    public static string ReadVersion(string dir)
    {
        try
        {
            string f = Path.Combine(dir, "service.bat");
            if (!File.Exists(f)) return null;
            var m = Regex.Match(File.ReadAllText(f), "LOCAL_VERSION=([^\"\\r\\n]+)");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
        catch { return null; }
    }

    static readonly Regex TokenRx = new Regex(@"\d+|[^\W\d_]+", RegexOptions.Compiled);

    /// <summary>Сравнение по словам и числам: «general (ALT)» идёт перед «general (ALT2)», числа сравниваются как числа.</summary>
    static int NaturalCompare(string a, string b)
    {
        var ta = TokenRx.Matches(a);
        var tb = TokenRx.Matches(b);
        int n = Math.Min(ta.Count, tb.Count);
        for (int i = 0; i < n; i++)
        {
            string x = ta[i].Value, y = tb[i].Value;
            bool nx = char.IsDigit(x[0]), ny = char.IsDigit(y[0]);
            int c;
            if (nx && ny) c = long.TryParse(x, out long lx) && long.TryParse(y, out long ly) ? lx.CompareTo(ly) : string.CompareOrdinal(x, y);
            else if (nx != ny) c = nx ? -1 : 1;
            else c = string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        int d = ta.Count.CompareTo(tb.Count);
        return d != 0 ? d : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
