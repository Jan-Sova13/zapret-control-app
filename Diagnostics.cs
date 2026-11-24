using System.Diagnostics;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ZapretControlApp;

public enum Sev { Ok, Warn, Fail, Unknown }

public class CheckResult
{
    public string Title, Detail, Advice;
    public Sev Sev;
    public string Mark => Sev == Sev.Ok ? "✓" : Sev == Sev.Warn ? "⚠" : Sev == Sev.Fail ? "✗" : "●";
}

public class DiagInput
{
    public string ZapretPath, StrategyName, WinwsTail, LastStartError;
    public bool BypassRunning, ManageTimestamps;
    public int OwnPid;
    public List<Target> Targets;
}

public class DiagCheck
{
    public string Title;
    public bool IsNet, NeedsNet;
    public Func<CancellationToken, Task<CheckResult>> Run;
}

/// <summary>Только чтение: ничего не меняет в системе.</summary>
public static class DiagRunner
{
    static CheckResult R(Sev s, string detail, string advice = null) => new CheckResult { Sev = s, Detail = detail, Advice = advice };

    // Коды ошибок WinDivertOpen (Win32) по документации WinDivert
    static readonly Dictionary<int, string> WinDivertCodes = new Dictionary<int, string>
    {
        [2] = "Файлы драйвера WinDivert не найдены. Вероятно, их удалил антивирус: добавьте папку Zapret в исключения и нажмите «Обновить Zapret».",
        [5] = "Отказано в доступе: программа должна работать от имени администратора.",
        [87] = "WinDivert не принял фильтр (неверный параметр). Выберите другую стратегию или нажмите «Обновить Zapret».",
        [577] = "Windows не смогла проверить цифровую подпись драйвера WinDivert. Файл мог быть повреждён или заменён антивирусом: обновите Zapret. На Windows Server 2016 драйвер не загрузится при включённом Secure Boot.",
        [654] = "Загружена несовместимая версия драйвера WinDivert (от другой программы). Закройте программы с WinDivert, выключите и включите Zapret или перезагрузите компьютер.",
        [1275] = "Драйвер заблокирован: антивирус, защита Windows (в том числе «Целостность памяти») или виртуальная среда без поддержки драйверов.",
        [1753] = "Не запущена служба Windows «Base Filtering Engine» (BFE). Включите её в services.msc.",
    };

    public static List<DiagCheck> Build(DiagInput d) => new List<DiagCheck>
    {
        new DiagCheck { Title = "Права администратора", Run = ct => Task.FromResult(Admin()) },
        new DiagCheck { Title = "Папка Zapret", Run = ct => Task.FromResult(Folder(d)) },
        new DiagCheck { Title = "Драйвер WinDivert", Run = ct => Task.Run(() => Driver(d), ct).WaitAsync(ct) },
        new DiagCheck { Title = "Конфликтующие программы", Run = ct => Task.Run(() => Conflicts(d), ct).WaitAsync(ct) },
        new DiagCheck { Title = "VPN и системный прокси", Run = ct => Task.Run(VpnProxy, ct).WaitAsync(ct) },
        new DiagCheck { Title = "Интернет", IsNet = true, Run = Internet },
        new DiagCheck { Title = "DNS", NeedsNet = true, Run = CheckDns },
        new DiagCheck { Title = "Системное время", NeedsNet = true, Run = CheckTime },
        new DiagCheck { Title = "Доступность сервисов", NeedsNet = true, Run = ct => Services(d, ct) },
        new DiagCheck { Title = "Антивирус", Run = ct => Task.Run(() => Antivirus(d), ct).WaitAsync(ct) },
        new DiagCheck { Title = "Целостность памяти (HVCI)", Run = ct => Task.FromResult(Hvci(d)) },
        new DiagCheck { Title = "TCP timestamps", Run = ct => Task.Run(() => Timestamps(d), ct).WaitAsync(ct) },
    };

    /// <summary>Независимые проверки идут параллельно, каждая ограничена 10 с, всё вместе — 30 с.</summary>
    public static async Task RunAllAsync(List<DiagCheck> checks, Action<int, CheckResult> onResult, CancellationToken ct)
    {
        using var all = CancellationTokenSource.CreateLinkedTokenSource(ct);
        all.CancelAfter(TimeSpan.FromSeconds(30));
        var netSrc = new TaskCompletionSource<bool>();

        async Task RunOne(int i)
        {
            var c = checks[i];
            CheckResult r;
            try
            {
                if (c.NeedsNet && !await netSrc.Task)
                    r = R(Sev.Unknown, "Пропущено: нет доступа в интернет.", "Сначала восстановите подключение к сети.");
                else
                {
                    using var one = CancellationTokenSource.CreateLinkedTokenSource(all.Token);
                    one.CancelAfter(TimeSpan.FromSeconds(10));
                    r = await c.Run(one.Token);
                }
            }
            catch (OperationCanceledException)
            {
                r = R(Sev.Unknown, "Не удалось проверить: " + (ct.IsCancellationRequested ? "отменено." : "превышено время ожидания."));
            }
            catch (Exception ex)
            {
                Diag.Write("diag:" + c.Title, ex);
                r = R(Sev.Unknown, "Не удалось проверить: " + ex.Message);
            }
            r.Title = c.Title;
            if (c.IsNet) netSrc.TrySetResult(r.Sev == Sev.Ok);
            onResult(i, r);
        }

        var tasks = new List<Task>();
        for (int i = 0; i < checks.Count; i++)
        {
            int idx = i;
            tasks.Add(Task.Run(() => RunOne(idx)));
        }
        await Task.WhenAll(tasks);
        netSrc.TrySetResult(false);
    }

    public static string BuildReport(IEnumerable<CheckResult> results, string header)
    {
        var sb = new StringBuilder(header).AppendLine().AppendLine();
        foreach (var r in results)
        {
            sb.AppendLine(r.Mark + " " + r.Title + " — " + r.Detail);
            if (!string.IsNullOrEmpty(r.Advice)) sb.AppendLine("    Совет: " + r.Advice);
        }
        return sb.ToString();
    }

    static CheckResult Admin()
    {
        bool admin;
        try { admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { admin = false; }
        return admin
            ? R(Sev.Ok, "Программа запущена от имени администратора.")
            : R(Sev.Fail, "Нет прав администратора.", "Закройте программу и запустите снова: без прав драйвер WinDivert не загрузится (ошибка 5).");
    }

    static CheckResult Folder(DiagInput d)
    {
        if (string.IsNullOrWhiteSpace(d.ZapretPath))
            return R(Sev.Fail, "Папка Zapret не выбрана.", "Укажите папку на вкладке «Настройки» или скачайте Zapret кнопкой «Установить Zapret».");
        if (!File.Exists(Path.Combine(d.ZapretPath, "bin", "winws.exe")))
            return R(Sev.Fail, "В папке нет bin\\winws.exe.", "Проверьте путь или нажмите «Установить Zapret». Возможно, антивирус удалил файлы.");
        if (!Directory.Exists(Path.Combine(d.ZapretPath, "lists")))
            return R(Sev.Fail, "В папке нет подпапки lists.", "Нажмите «Установить Zapret», чтобы восстановить файлы.");

        string[] bats = Directory.GetFiles(d.ZapretPath, "*.bat");
        int count = bats.Count(b => !Path.GetFileName(b).StartsWith("service", StringComparison.OrdinalIgnoreCase));
        if (count == 0) return R(Sev.Fail, "Не найдено ни одной стратегии (general*.bat).", "Нажмите «Установить Zapret».");

        bool cyr = d.ZapretPath.Any(ch => ch > 127);
        bool onedrive = d.ZapretPath.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0;
        if (cyr || onedrive)
            return R(Sev.Warn, "Путь «" + d.ZapretPath + "» содержит " + (cyr ? "кириллицу" : "OneDrive") + ".",
                "Перенесите Zapret в простую папку, например C:\\zapret: такие пути часто ломают запуск winws.");
        return R(Sev.Ok, "Папка в порядке, стратегий: " + count + ".");
    }

    static CheckResult Driver(DiagInput d)
    {
        if (!Strategy.IsValidZapretDir(d.ZapretPath)) return R(Sev.Unknown, "Папка Zapret не выбрана — проверять нечего.");

        string bin = Path.Combine(d.ZapretPath, "bin");
        var missing = new List<string>();
        if (!File.Exists(Path.Combine(bin, "WinDivert64.sys"))) missing.Add("WinDivert64.sys");
        if (!File.Exists(Path.Combine(bin, "WinDivert.dll"))) missing.Add("WinDivert.dll");
        if (missing.Count > 0)
            return R(Sev.Fail, "Рядом с winws.exe нет файлов: " + string.Join(", ", missing) + ".",
                "Вероятно, их удалил антивирус. Добавьте папку Zapret в исключения и нажмите «Обновить Zapret».");

        int code = FindWinDivertCode(d.WinwsTail);
        if (code > 0 && WinDivertCodes.TryGetValue(code, out var advice))
            return R(Sev.Fail, "winws сообщил об ошибке WinDivert " + code + ".", advice);

        bool svc = ServiceRunning("WinDivert") || ServiceRunning("WinDivert14");
        if (d.BypassRunning && !svc)
            return R(Sev.Warn, "Zapret включён, но служба драйвера WinDivert не запущена.", "Выключите и снова включите Zapret; если повторяется — смотрите журнал.");
        return R(Sev.Ok, d.BypassRunning ? "Файлы драйвера на месте, служба WinDivert работает." : "Файлы драйвера на месте.");
    }

    static int FindWinDivertCode(string tail)
    {
        if (string.IsNullOrEmpty(tail)) return 0;
        foreach (var line in tail.Split('\n'))
        {
            if (!Regex.IsMatch(line, "windivert|error|fail|ошиб", RegexOptions.IgnoreCase)) continue;
            foreach (Match m in Regex.Matches(line, @"\b\d{1,4}\b"))
                if (int.TryParse(m.Value, out int n) && WinDivertCodes.ContainsKey(n)) return n;
        }
        return 0;
    }

    static bool ServiceRunning(string name)
    {
        var r = SystemTools.Run("sc.exe", "query " + name, 6000);
        return r.Code == 0 && r.Output.Contains("RUNNING");
    }

    static CheckResult Conflicts(DiagInput d)
    {
        var found = new List<string>();
        int foreign = SystemTools.ForeignWinwsCount(d.OwnPid);
        if (foreign > 0) found.Add("запущен другой winws.exe (" + foreign + " шт.)");
        if (SystemTools.ZapretServiceRunning()) found.Add("работает служба Windows «zapret» (из service.bat)");
        foreach (var name in new[] { "goodbyedpi", "ciadpi", "byedpi", "powertunnel", "spoofdpi" })
        {
            var ps = Process.GetProcessesByName(name);
            if (ps.Length > 0) found.Add("запущен " + name + ".exe");
            foreach (var p in ps) p.Dispose();
        }
        if (found.Count > 0)
            return R(Sev.Fail, string.Join("; ", found) + ".", "Остановите другие программы обхода: два перехватчика трафика одновременно конфликтуют.");

        if (!d.BypassRunning && (ServiceRunning("WinDivert") || ServiceRunning("WinDivert14")))
            return R(Sev.Warn, "Служба WinDivert работает, хотя Zapret выключен.", "Драйвером пользуется другая программа (например, другая программа обхода). Закройте её или перезагрузите компьютер.");
        return R(Sev.Ok, "Других программ обхода и перехватчиков не найдено.");
    }

    static CheckResult VpnProxy()
    {
        var notes = new List<string>();
        var route = SystemTools.InspectDefaultRoute();
        if (route != null && route.IsVpn) notes.Add("интернет идёт через VPN-адаптер «" + route.Name + "» (режим TUN)");

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            if (key != null)
            {
                if (key.GetValue("ProxyEnable") is int en && en == 1) notes.Add("включён системный прокси " + key.GetValue("ProxyServer"));
                if (key.GetValue("AutoConfigURL") is string pac && pac.Length > 0) notes.Add("задан скрипт прокси (PAC) " + pac);
            }
        }
        catch { }

        if (notes.Count == 0)
            return R(Sev.Ok, route != null ? "Маршрут в интернет: " + route.Name + ". VPN в режиме TUN и системный прокси не найдены." : "VPN в режиме TUN и системный прокси не найдены.");
        return R(Sev.Warn, string.Join("; ", notes) + ".",
            "Трафик через VPN или прокси идёт мимо Zapret. Для проверки отключите VPN/прокси на время работы Zapret.");
    }

    static async Task<CheckResult> Internet(CancellationToken ct)
    {
        var r = await NetInfo.ProbeAsync(ct);
        return r.Ok
            ? R(Sev.Ok, "Интернет доступен (проверено через " + r.Host + ").")
            : R(Sev.Fail, "Нет доступа в интернет.", "Проверьте кабель или Wi-Fi, роутер и подключение. Остальные сетевые проверки пропущены.");
    }

    static bool Suspicious(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (b.Length != 4) return false;
        return b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }

    static async Task<List<IPAddress>> SysResolve(string domain, CancellationToken ct)
    {
        try
        {
            var all = await System.Net.Dns.GetHostAddressesAsync(domain, ct);
            return all.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    static async Task<List<IPAddress>> DohResolve(HttpClient http, string domain, CancellationToken ct)
    {
        foreach (var url in new[]
        {
            "https://1.1.1.1/dns-query?name=" + domain + "&type=A",
            "https://dns.google/resolve?name=" + domain + "&type=A",
        })
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Accept.ParseAdd("application/dns-json");
                using var resp = await http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) continue;
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                var res = new List<IPAddress>();
                if (doc.RootElement.TryGetProperty("Answer", out var ans))
                    foreach (var a in ans.EnumerateArray())
                        if (a.TryGetProperty("type", out var t) && t.GetInt32() == 1 && IPAddress.TryParse(a.GetProperty("data").GetString(), out var ip))
                            res.Add(ip);
                if (res.Count > 0) return res;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }
        return null;
    }

    static bool SamePrefix(IPAddress a, IPAddress b)
    {
        var x = a.GetAddressBytes();
        var y = b.GetAddressBytes();
        return x.Length == 4 && y.Length == 4 && x[0] == y[0] && x[1] == y[1];
    }

    static async Task<CheckResult> CheckDns(CancellationToken ct)
    {
        string[] domains = { "youtube.com", "discord.com", "instagram.com", "telegram.org" };
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(3) }) { Timeout = TimeSpan.FromSeconds(4) };
        var sys = await Task.WhenAll(domains.Select(x => SysResolve(x, ct)));
        var doh = await Task.WhenAll(domains.Select(x => DohResolve(http, x, ct)));

        var bad = new List<string>();
        var diff = new List<string>();
        var firstIps = new List<(string Domain, IPAddress Ip)>();
        int dohOk = 0;
        for (int i = 0; i < domains.Length; i++)
        {
            if (sys[i] == null || sys[i].Count == 0) { diff.Add(domains[i] + " (система не получила адрес)"); continue; }
            firstIps.Add((domains[i], sys[i][0]));
            foreach (var ip in sys[i].Where(Suspicious)) bad.Add(domains[i] + " → " + ip);
            if (doh[i] == null) continue;
            dohOk++;
            if (!sys[i].Any(s => doh[i].Any(h => s.Equals(h) || SamePrefix(s, h)))) diff.Add(domains[i]);
        }
        var stub = firstIps.GroupBy(x => x.Ip.ToString()).FirstOrDefault(g => g.Count() >= 2);
        if (stub != null) bad.Add("один адрес " + stub.Key + " для " + string.Join(", ", stub.Select(x => x.Domain)));

        const string advice = "Включите шифрованный DNS (DoH) в Windows 11: Параметры → Сеть и интернет → ваше подключение → Назначение DNS-сервера. Либо смените DNS на 1.1.1.1 или 8.8.8.8. Программа настройки DNS не меняет.";
        if (bad.Count > 0) return R(Sev.Fail, "DNS подменяется провайдером: " + string.Join("; ", bad) + ".", advice);
        if (dohOk == 0) return R(Sev.Unknown, "Не удалось проверить: шифрованный DNS (DoH) недоступен для сравнения.");
        if (diff.Count > 0) return R(Sev.Warn, "Ответы системного DNS отличаются от DoH: " + string.Join(", ", diff) + ". У CDN так бывает, но это может быть и подменой.", advice);
        return R(Sev.Ok, "Ответы системного DNS совпадают с DoH.");
    }

    static async Task<CheckResult> CheckTime(CancellationToken ct)
    {
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(3) }) { Timeout = TimeSpan.FromSeconds(4) };
        DateTimeOffset? date = null;
        string via = "HTTPS";
        foreach (var url in new[] { "https://www.microsoft.com/", "http://www.msftconnecttest.com/connecttest.txt" })
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, url);
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                date = resp.Headers.Date;
                if (date != null) { via = url.StartsWith("https") ? "HTTPS" : "HTTP"; break; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }
        if (date == null) return R(Sev.Unknown, "Не удалось проверить: сервер не вернул дату.");

        double min = Math.Abs((DateTimeOffset.UtcNow - date.Value).TotalMinutes);
        if (min > 5)
            return R(Sev.Warn, "Время компьютера отличается от точного примерно на " + (int)min + " мин (сверка по " + via + ").",
                "Неверное время ломает TLS: сайты не открываются, а Zapret выглядит нерабочим. Параметры → Время и язык → Дата и время → «Синхронизировать».");
        return R(Sev.Ok, "Время совпадает с серверным (расхождение меньше 5 минут).");
    }

    static async Task<CheckResult> Services(DiagInput d, CancellationToken ct)
    {
        if (d.Targets == null || d.Targets.Count == 0) return R(Sev.Unknown, "Не выбраны сервисы и домены для проверки (вкладка автоподбора).");
        var res = await Tester.Check(d.Targets, ct);
        var lines = new List<string>();

        foreach (var g in res.GroupBy(x => x.Target.Group ?? x.Target.Domain))
        {
            string name = TestTargets.Services.FirstOrDefault(s => s.Short == g.Key)?.Name ?? g.Key;
            int bad = g.Count(x => !x.Ok);
            if (bad == 0) continue;

            lines.Add(name + ": не открылось " + bad + " из " + g.Count());
        }
        string mode = d.BypassRunning ? "Zapret включён (" + d.StrategyName + ")" : "Zapret выключен";
        if (lines.Count == 0) return R(Sev.Ok, "Все выбранные сервисы открываются; " + mode + ".");
        if (!d.BypassRunning) return R(Sev.Warn, string.Join("; ", lines) + "; " + mode + ".", "Включите Zapret и повторите диагностику.");
        return R(Sev.Fail, string.Join("; ", lines) + "; " + mode + ".", "Запустите автоподбор и выберите другую стратегию; проверьте остальные пункты отчёта.");
    }

    static CheckResult Antivirus(DiagInput d)
    {
        var r = SystemTools.PowerShell(
            "try { Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct | ForEach-Object { 'AV|' + $_.displayName } } catch { 'AVERR|' + $_.Exception.Message }; " +
            "try { (Get-MpPreference).ExclusionPath | ForEach-Object { 'EX|' + $_ } } catch { 'EXERR|' + $_.Exception.Message }; exit 0", 25000);
        var avs = new List<string>();
        var ex = new List<string>();
        bool avErr = false, exErr = false;
        foreach (var raw in r.Output.Replace("\r", "").Split('\n'))
        {
            string l = raw.Trim();
            if (l.StartsWith("AV|")) avs.Add(l.Substring(3));
            else if (l.StartsWith("EX|")) ex.Add(l.Substring(3));
            else if (l.StartsWith("AVERR|")) avErr = true;
            else if (l.StartsWith("EXERR|")) exErr = true;
        }
        if (r.Code != 0 || (avErr && avs.Count == 0)) return R(Sev.Unknown, "Не удалось определить установленный антивирус (Центр безопасности Windows недоступен).");
        if (avs.Count == 0) return R(Sev.Ok, "Антивирусы не обнаружены.");

        string list = string.Join(", ", avs.Distinct());
        bool defender = avs.Any(a => a.IndexOf("defender", StringComparison.OrdinalIgnoreCase) >= 0);
        if (!defender)
            return R(Sev.Warn, "Установлен: " + list + ".", "Проверьте вручную, что папка Zapret добавлена в исключения этого антивируса, иначе он может удалять файлы WinDivert.");
        if (exErr || string.IsNullOrWhiteSpace(d.ZapretPath))
            return R(Sev.Unknown, "Установлен: " + list + ". Исключения Защитника прочитать не удалось.");

        string zp = Path.GetFullPath(d.ZapretPath).TrimEnd('\\');
        bool excluded = ex.Any(e =>
        {
            string x = Environment.ExpandEnvironmentVariables(e).TrimEnd('\\');
            return x.Length > 0 && (zp.Equals(x, StringComparison.OrdinalIgnoreCase) || zp.StartsWith(x + "\\", StringComparison.OrdinalIgnoreCase));
        });
        return excluded
            ? R(Sev.Ok, "Установлен: " + list + ". Папка Zapret есть в исключениях Защитника.")
            : R(Sev.Warn, "Установлен: " + list + ". Папки Zapret нет в исключениях Защитника.",
                "Защитник может удалить WinDivert. Добавьте папку в исключения вручную (Безопасность Windows → Защита от вирусов → Управление настройками). Программа исключения не добавляет.");
    }

    /// <summary>Параметр реестра, которым Windows хранит состояние «Целостности памяти» (документирован Microsoft).</summary>
    static CheckResult Hvci(DiagInput d)
    {
        int? enabled = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            if (key?.GetValue("Enabled") is int v) enabled = v;
        }
        catch { }

        if (enabled != 1) return R(Sev.Ok, "«Целостность памяти» выключена.");
        int code = FindWinDivertCode(d.WinwsTail);
        if (code == 1275 || code == 577)
            return R(Sev.Fail, "«Целостность памяти» включена и, судя по ошибке winws, блокирует драйвер.",
                "Безопасность Windows → Безопасность устройства → Изоляция ядра → «Целостность памяти»: выключите и перезагрузите компьютер. Программа этого не меняет.");
        if (d.BypassRunning) return R(Sev.Ok, "«Целостность памяти» включена, но драйвер загружен — проблем не видно.");
        return R(Sev.Warn, "«Целостность памяти» включена: на некоторых системах она блокирует драйвер WinDivert (ошибка 1275).",
            "Если Zapret не запускается, выключите её: Безопасность Windows → Безопасность устройства → Изоляция ядра. Программа этого не меняет.");
    }

    static CheckResult Timestamps(DiagInput d)
    {
        bool? on = SystemTools.TimestampsEnabled();
        string mode = d.ManageTimestamps ? "программа включает их на время работы и возвращает обратно" : "программа ими не управляет";
        if (on == null) return R(Sev.Unknown, "Не удалось определить состояние TCP timestamps.");
        if (on == true) return R(Sev.Ok, "TCP timestamps включены (" + mode + ").");
        if (d.ManageTimestamps && !d.BypassRunning) return R(Sev.Ok, "TCP timestamps выключены; " + mode + ".");
        if (d.ManageTimestamps) return R(Sev.Warn, "Zapret включён, но TCP timestamps выключены.", "Часть стратегий без них не работает: перезапустите Zapret.");
        return R(Sev.Warn, "TCP timestamps выключены, программа ими не управляет.",
            "Часть стратегий требует timestamps. Включите опцию управления в «Настройки» (продвинутый режим).");
    }
}
