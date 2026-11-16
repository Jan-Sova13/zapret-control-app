using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ZapretControlApp;

public enum BypassState { Off, Working, Problems, NoNetwork, Crashed }

public static class NetInfo
{
    /// <summary>Отпечаток сетевых подключений с маршрутом в интернет: меняется при смене Wi-Fi/кабеля/адреса.</summary>
    public static string Signature()
    {
        var items = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var props = ni.GetIPProperties();
                if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))) continue;
                foreach (var u in props.UnicastAddresses)
                    if (u.Address.AddressFamily == AddressFamily.InterNetwork) items.Add(ni.Id + "=" + u.Address);
            }
        }
        catch { }
        items.Sort(StringComparer.Ordinal);
        return string.Join(";", items);
    }

    /// <summary>Есть ли интернет вообще: обычный HTTP к нейтральным адресам (DPI их не трогает).</summary>
    public static async Task<(bool Ok, string Host)> ProbeAsync(CancellationToken ct)
    {
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(3) })
        {
            Timeout = TimeSpan.FromSeconds(4),
        };
        foreach (var (url, mark) in new[]
        {
            ("http://www.msftconnecttest.com/connecttest.txt", "Microsoft Connect Test"),
            ("http://detectportal.firefox.com/success.txt", "success"),
        })
        {
            try
            {
                string body = await http.GetStringAsync(url, ct);
                if (body.Contains(mark)) return (true, new Uri(url).Host);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }
        return (false, null);
    }
}
