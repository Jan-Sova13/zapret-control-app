using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapretControlApp;

public enum ProbeKind { Page, Download, WebSocket }

public class ProbeSpec
{
    public string Label;
    public ProbeKind Kind;
    public string Url;                              // готовый адрес…
    public string FromLabel, Pattern, Prefix, Prefer; // …или адрес из ответа другой пробы (Pattern — регулярка, Prefer — предпочтительная подстрока)
    public long Cap = 262144;                       // сколько байт читать
    public bool Speed;                              // учитывать скорость и порог
}

public class ProbeStat
{
    public string Label, Url, Err;
    public bool Ok, Skipped, Measured;
    public long Bytes;
    public int TtfbMs, Stalls;
    public double KBps;
}

public class SvcRepeat
{
    public string Short, Name;
    public int Repeat;
    public bool Pass, Slow;
    public double? KBps;
    public List<ProbeStat> Probes = new List<ProbeStat>();
}

public class DeepResult : TestResult
{
    public List<SvcRepeat> Runs = new List<SvcRepeat>();
    public int Repeats, StableRepeats, ServicesPassed, ServicesTotal;
    public double AvgKBps;
    public string Row, Tip;
    public double Rank => ServicesPassed * 1e9 + StableRepeats * 1e6 + AvgKBps;
}

public class DeepService
{
    public string Short, Name;
    public ProbeSpec[] Specs;
}

/// <summary>Набор проб по сервисам. Таблица легко меняется: адреса, порядок и размеры читаются только отсюда.</summary>
public static class DeepProbes
{
    const long PageCap = 262144, BulkCap = 1048576;

    static ProbeSpec Page(string label, string url, long cap = PageCap)
        => new ProbeSpec { Label = label, Kind = ProbeKind.Page, Url = url, Cap = cap };

    static ProbeSpec Bulk(string label, string from, string pattern, string prefix, string prefer = null, bool speed = true)
        => new ProbeSpec { Label = label, Kind = ProbeKind.Download, FromLabel = from, Pattern = pattern, Prefix = prefix, Prefer = prefer, Cap = BulkCap, Speed = speed };

    public static readonly Dictionary<string, ProbeSpec[]> Table = new Dictionary<string, ProbeSpec[]>
    {
        ["YT"] = new[]
        {
            Page("Главная", "https://www.youtube.com/", 524288),
            Bulk("Скрипт плеера", "Главная", "\"jsUrl\":\"(/s/player/[^\"]+\\.js)\"", "https://www.youtube.com"),
            new ProbeSpec { Label = "Картинка", Kind = ProbeKind.Download, Url = "https://i.ytimg.com/vi/jNQXAC9IVRw/hqdefault.jpg", Cap = BulkCap },
        },
        ["Disc"] = new[]
        {
            Page("Клиент", "https://discord.com/app"),
            Bulk("Скрипт клиента", "Клиент", "src=\"(/assets/[^\"]+\\.js)\"", "https://discord.com", "web."),
            new ProbeSpec { Label = "Gateway", Kind = ProbeKind.WebSocket, Url = "wss://gateway.discord.gg/?v=10&encoding=json" },
        },
        ["CF"] = new[]
        {
            Page("Главная", "https://cloudflareinsights.com/"),
            new ProbeSpec { Label = "Страница ECH", Kind = ProbeKind.Download, Url = "https://cloudflare-ech.com/", Cap = BulkCap, Speed = true },
            new ProbeSpec { Label = "Скрипт", Kind = ProbeKind.Download, Url = "https://static.cloudflareinsights.com/beacon.min.js", Cap = BulkCap },
        },
    };

    public static DeepService ForDomain(string domain)
        => new DeepService { Short = domain, Name = domain, Specs = new[] { Page("Главная", "https://" + domain + "/", BulkCap) } };
}

public static class DeepTester
{
    public static string FmtSpeed(double kb)
        => kb >= 1024
            ? (kb / 1024).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " МБ/с"
            : (int)Math.Round(kb) + " КБ/с";

    static HttpClient NewHttp()
    {
        var h = new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(4), AutomaticDecompression = System.Net.DecompressionMethods.None })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) ZapretControlApp/2.0");
        return h;
    }

    static string FindUrl(string html, ProbeSpec s)
    {
        var vals = Regex.Matches(html, s.Pattern).Cast<Match>()
            .Select(m => m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value : m.Value).ToList();
        if (vals.Count == 0) return null;
        string pick = string.IsNullOrEmpty(s.Prefer) ? null : vals.FirstOrDefault(v => v.Contains(s.Prefer));
        pick ??= vals[0];
        pick = pick.Replace("\\/", "/").Replace("&amp;", "&");
        if (pick.StartsWith("//")) pick = "https:" + pick;
        else if (pick.StartsWith("/") && s.Prefix != null) pick = s.Prefix + pick;
        return pick.StartsWith("http") ? pick : null;
    }

    static async Task<ProbeStat> RunProbeAsync(HttpClient http, ProbeSpec spec, Dictionary<string, string> pages, CancellationToken ct)
    {
        var st = new ProbeStat { Label = spec.Label };
        string url = spec.Url;
        if (url == null)
        {
            if (!pages.TryGetValue(spec.FromLabel, out var html)) { st.Err = "страница не открылась"; return st; }
            url = FindUrl(html, spec);
            if (url == null) { st.Skipped = true; st.Ok = true; st.Err = "адрес не найден на странице"; return st; }
        }
        st.Url = url;

        if (spec.Kind == ProbeKind.WebSocket) await Hello(st, url, ct);
        else await Fetch(http, st, spec, url, pages, ct);
        return st;
    }

    /// <summary>Скачивает до Cap байт, но не дольше 10 с. Зависание — пауза без данных дольше 3 с. Вся проба ограничена 12 с.</summary>
    static async Task Fetch(HttpClient http, ProbeStat st, ProbeSpec spec, string url, Dictionary<string, string> pages, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(12));
        var sw = Stopwatch.StartNew();
        try
        {
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            var buf = new byte[16384];
            var ms = spec.Kind == ProbeKind.Page ? new MemoryStream() : null;
            long total = 0, first = -1, last = sw.ElapsedMilliseconds;
            bool eof = false;
            while (total < spec.Cap && sw.ElapsedMilliseconds < 10000)
            {
                int n = await stream.ReadAsync(buf, 0, buf.Length, cts.Token);
                if (n <= 0) { eof = true; break; }
                long now = sw.ElapsedMilliseconds;
                if (first < 0) first = now;
                else if (now - last > 3000) st.Stalls++;
                last = now;
                total += n;
                ms?.Write(buf, 0, n);
            }

            st.Bytes = total;
            st.TtfbMs = (int)(first < 0 ? sw.ElapsedMilliseconds : first);
            bool done = eof || total >= spec.Cap;
            st.Ok = total > 0 || eof;
            if (spec.Speed && (total >= 256 * 1024 || (!done && total > 0)))
            {
                double secs = Math.Max(0.05, (sw.ElapsedMilliseconds - Math.Max(0, first)) / 1000.0);
                st.KBps = total / 1024.0 / secs;
                st.Measured = true;
            }
            else if (!done && total == 0) { st.Ok = false; st.Err = "нет данных за 10 с"; }

            if (st.Ok && ms != null) pages[spec.Label] = Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { st.Err = "превышено время (12 с)"; }
        catch (Exception ex) { st.Err = "обрыв: " + ex.Message; }
    }

    /// <summary>Рукопожатие WebSocket: HELLO (op 10) от шлюза Discord за 5 с.</summary>
    static async Task Hello(ProbeStat st, string url, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var ws = new ClientWebSocket();
        ws.Options.Proxy = null;
        try
        {
            using var conn = CancellationTokenSource.CreateLinkedTokenSource(ct);
            conn.CancelAfter(TimeSpan.FromSeconds(8));
            await ws.ConnectAsync(new Uri(url), conn.Token);
            using var rcv = CancellationTokenSource.CreateLinkedTokenSource(ct);
            rcv.CancelAfter(TimeSpan.FromSeconds(5));
            var buf = new byte[4096];
            var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), rcv.Token);
            st.TtfbMs = (int)sw.ElapsedMilliseconds;
            st.Ok = Encoding.UTF8.GetString(buf, 0, r.Count).Contains("\"op\":10");
            if (!st.Ok) st.Err = "HELLO не получен";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { st.Err = "HELLO не получен за 5 с"; }
        catch (Exception ex) { st.Err = "обрыв: " + ex.Message; }
    }

    static async Task<SvcRepeat> RunServiceAsync(DeepService svc, double thresholdKB, int repeat, CancellationToken ct)
    {
        var rep = new SvcRepeat { Short = svc.Short, Name = svc.Name, Repeat = repeat };
        using var http = NewHttp();
        var pages = new Dictionary<string, string>();
        foreach (var spec in svc.Specs) rep.Probes.Add(await RunProbeAsync(http, spec, pages, ct));

        rep.Pass = rep.Probes.All(p => p.Ok && p.Stalls == 0);
        var meas = rep.Probes.Where(p => p.Measured).ToList();
        if (meas.Count > 0)
        {
            rep.KBps = meas.Average(p => p.KBps);
            if (rep.KBps < thresholdKB) { rep.Slow = true; rep.Pass = false; }
        }
        return rep;
    }

    static string Reason(SvcRepeat r)
    {
        if (r.Slow && r.Probes.All(p => p.Ok && p.Stalls == 0)) return "медленно";
        if (r.Probes.Any(p => p.Stalls > 0)) return "зависания";
        if (r.Probes.Any(p => !p.Ok && p.Err != null && p.Err.StartsWith("обрыв"))) return "обрыв";
        return "не открылся";
    }

    /// <summary>Этап 2 для одной стратегии (winws уже запущен вызывающим): проба каждого сервиса повторяется repeats раз.</summary>
    public static async Task<DeepResult> EvaluateAsync(Strategy st, int order, List<DeepService> svcs, double thresholdKB, int repeats,
        Action<int> onRepeat, CancellationToken ct)
    {
        var dr = new DeepResult { Name = st.Name, Strategy = st, Order = order, Repeats = repeats, ServicesTotal = svcs.Count, Total = svcs.Count };
        for (int r = 1; r <= repeats; r++)
        {
            onRepeat?.Invoke(r);
            var runs = await Task.WhenAll(svcs.Select(s => RunServiceAsync(s, thresholdKB, r, ct)));
            dr.Runs.AddRange(runs);
            if (runs.All(x => x.Pass)) dr.StableRepeats++;
        }
        Summarize(dr, svcs, thresholdKB);
        return dr;
    }

    static void Summarize(DeepResult dr, List<DeepService> svcs, double thresholdKB)
    {
        var parts = new List<string>();
        var speeds = new List<double>();
        var tip = new StringBuilder();
        foreach (var svc in svcs)
        {
            var runs = dr.Runs.Where(x => x.Short == svc.Short).OrderBy(x => x.Repeat).ToList();
            int pass = runs.Count(x => x.Pass);
            var withSpeed = runs.Where(x => x.KBps.HasValue).Select(x => x.KBps.Value).ToList();
            double avg = withSpeed.Count > 0 ? withSpeed.Average() : 0;
            if (withSpeed.Count > 0) speeds.Add(avg);
            if (pass == dr.Repeats) dr.ServicesPassed++;

            if (pass == dr.Repeats) parts.Add(svc.Short + " " + (withSpeed.Count > 0 ? FmtSpeed(avg) : "ок"));
            else
            {
                var reasons = runs.Where(x => !x.Pass).Select(Reason).Distinct().ToList();
                if (reasons.Count == 1)
                    parts.Add(svc.Short + " " + reasons[0] + (reasons[0] == "медленно" && withSpeed.Count > 0 ? " (" + FmtSpeed(withSpeed.Min()) + ")" : ""));
                else parts.Add(svc.Short + " нестабильно (" + pass + "/" + dr.Repeats + ")");
            }

            tip.AppendLine(svc.Name + ":");
            foreach (var run in runs)
            {
                tip.Append("  #" + run.Repeat + " " + (run.Pass ? "✓ " : "✗ "));
                foreach (var p in run.Probes)
                {
                    string host = "";
                    try { if (p.Url != null) host = " (" + new Uri(p.Url).Host + ")"; } catch { }
                    tip.Append(p.Label + host + ": " + (p.Skipped ? "пропущено" : p.Ok ? "ок, " + p.TtfbMs + " мс" + (p.Measured ? ", " + FmtSpeed(p.KBps) : "") : p.Err) +
                               (p.Stalls > 0 ? ", зависаний: " + p.Stalls : "") + "; ");
                }
                if (run.Slow) tip.Append("медленно (порог " + FmtSpeed(thresholdKB) + ")");
                tip.AppendLine();
            }
        }

        dr.AvgKBps = speeds.Count > 0 ? speeds.Average() : 0;
        dr.Ok = dr.ServicesPassed;
        string mark = dr.ServicesPassed == dr.ServicesTotal && dr.StableRepeats == dr.Repeats ? "✓"
            : dr.ServicesPassed == 0 && dr.StableRepeats == 0 ? "✗" : "~";
        dr.Row = mark + " " + dr.Name + " · " + string.Join(" · ", parts) + " · стабильно " + dr.StableRepeats + "/" + dr.Repeats;
        dr.Tip = tip.ToString().TrimEnd();
    }
}
