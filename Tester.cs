using System.Net;

namespace ZapretControlApp;

public class TestResult
{
    public string Name;
    public int Ok;
    public int Total;
    public string Note;
    public Strategy Strategy;   // null для строки «без обхода»
    public int Order;
    public List<string> FailedGroups = new List<string>();   // короткие имена сервисов (YT, Disc…)
    public List<string> FailedCustom = new List<string>();   // свои домены, которые не открылись
}

/// <summary>Один адрес для проверки. Group — короткое имя сервиса или null для своего домена.</summary>
public class Target
{
    public string Domain;
    public string Group;
}

public static class TestTargets
{
    public class Service
    {
        public string Name, Short;
        public string[] Domains;
    }

    public static readonly Service[] Services =
    {
        new Service { Name = "YouTube",   Short = "YT",   Domains = new[] { "www.youtube.com", "i.ytimg.com", "youtubei.googleapis.com" } },
        new Service { Name = "Discord",   Short = "Disc", Domains = new[] { "discord.com", "gateway.discord.gg", "cdn.discordapp.com", "media.discordapp.net" } },
        new Service { Name = "Instagram", Short = "Inst", Domains = new[] { "www.instagram.com", "i.instagram.com", "static.cdninstagram.com" } },
        new Service { Name = "Telegram",  Short = "TG",   Domains = new[] { "web.telegram.org", "telegram.org", "t.me" } },
    };

    public static string[] ServiceNames => Services.Select(s => s.Name).ToArray();
}

public static class Tester
{
    /// <summary>Пробует открыть https://домен/ и прочитать до 32 КБ ответа. Любой HTTP-ответ считается успехом:
    /// важно, что соединение не оборвано и данные идут (именно так DPI обычно «душит» сайты).</summary>
    public static async Task<List<(Target Target, bool Ok)>> Check(IEnumerable<Target> targets, CancellationToken ct)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(4),
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromSeconds(1),
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(7) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) ZapretControlApp/1.1");

        var list = targets.ToList();
        var oks = await Task.WhenAll(list.Select(t => CheckOne(http, t.Domain, ct)));

        var res = new List<(Target Target, bool Ok)>();
        for (int i = 0; i < list.Count; i++) res.Add((list[i], oks[i]));
        return res;
    }

    static async Task<bool> CheckOne(HttpClient http, string domain, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(6));
            using var resp = await http.GetAsync("https://" + domain + "/", HttpCompletionOption.ResponseHeadersRead, cts.Token);
            using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            var buf = new byte[8192];
            int total = 0, n;
            while (total < 32768 && (n = await stream.ReadAsync(buf, 0, buf.Length, cts.Token)) > 0) total += n;
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
