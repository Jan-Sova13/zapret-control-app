using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapretControlApp;

/// <summary>Все обращения к системе (процессы, службы, сеть, планировщик) собраны в одном месте.</summary>
public static class SystemTools
{
    public const string TaskName = "Zapret Control App";
    const string LegacyTaskName = "ZapretControl";

    public static (int Code, string Output) Run(string file, string args, int timeoutMs = 20000)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                return (-1, "timeout");
            }
            p.WaitForExit();
            return (p.ExitCode, o.Result + e.Result);
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    public static (int Code, string Output) PowerShell(string script, int timeoutMs = 30000)
    {
        string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; " + script));
        return Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -OutputFormat Text -EncodedCommand " + enc, timeoutMs);
    }

    /// <summary>TCP timestamps нужны части стратегий zapret. null — состояние определить не удалось.</summary>
    public static bool? TimestampsEnabled()
    {
        var r = PowerShell("(Get-NetTCPSetting -SettingName Internet).Timestamps");
        if (r.Code != 0) return null;
        string o = r.Output.Trim();
        if (o.StartsWith("Enabled", StringComparison.OrdinalIgnoreCase)) return true;
        if (o.StartsWith("Disabled", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    public static void SetTimestamps(bool on)
        => Run("netsh.exe", "interface tcp set global timestamps=" + (on ? "enabled" : "disabled"), 15000);

    /// <summary>Останавливает и удаляет службу драйвера WinDivert, но только если она реально остановилась
    /// (чтобы не сломать другой софт, который ею пользуется).</summary>
    public static void CleanupWinDivert()
    {
        foreach (var name in new[] { "WinDivert", "WinDivert14" })
        {
            var q = Run("sc.exe", "query " + name, 6000);
            if (q.Code != 0) continue; // службы нет
            if (!q.Output.Contains("STOPPED")) Run("sc.exe", "stop " + name, 10000);
            q = Run("sc.exe", "query " + name, 6000);
            if (q.Code == 0 && q.Output.Contains("STOPPED")) Run("sc.exe", "delete " + name, 6000);
        }
    }

    public static bool AnyWinwsRunning()
    {
        var ps = Process.GetProcessesByName("winws");
        bool any = ps.Length > 0;
        foreach (var p in ps) p.Dispose();
        return any;
    }

    public static List<Process> ForeignWinws(int ownPid)
    {
        var res = new List<Process>();
        foreach (var p in Process.GetProcessesByName("winws"))
        {
            if (p.Id != ownPid) res.Add(p); else p.Dispose();
        }
        return res;
    }

    public static bool ZapretServiceRunning()
    {
        var r = Run("sc.exe", "query zapret", 6000);
        return r.Code == 0 && r.Output.Contains("RUNNING");
    }

    static readonly string[] AdapterHints =
    {
        "wintun", "tap-windows", "tap0901", "wireguard", "openvpn", "nordlynx", "hiddify", "sing-box",
        "v2ray", "xray", "clash", "mihomo", "nekoray", "amnezia", "incy", "warp", "outline", "vpn", "tunnel",
    };

    static readonly string[] AdapterIgnore =
    {
        "teredo", "isatap", "6to4", "loopback", "vethernet", "hyper-v", "wsl", "virtualbox", "vmware",
        "bluetooth", "wan miniport", "radmin",
    };

    static readonly Dictionary<string, bool?> HardwareCache = new Dictionary<string, bool?>();

    public class RouteInfo
    {
        public string Name, Description, Type;
        public bool? Hardware;
        public bool IsVpn;
    }

    public static int ForeignWinwsCount(int ownPid)
    {
        var list = ForeignWinws(ownPid);
        int n = list.Count;
        foreach (var p in list) p.Dispose();
        return n;
    }

    /// <summary>Находит адаптер, через который система отправляет трафик в интернет, и решает, VPN ли это.
    /// Запущенный VPN-клиент, прокси-режим и адаптеры без маршрута в интернет конфликта с zapret не создают.
    /// VPN считается, если адаптер виртуальный по типу, имя/описание похоже на туннель или у адаптера нет физического устройства.</summary>
    public static RouteInfo InspectDefaultRoute()
    {
        try
        {
            string localIp;
            using (var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                sock.Connect(new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53));   // пакет не уходит, выбирается только маршрут
                localIp = ((IPEndPoint)sock.LocalEndPoint).Address.ToString();
            }
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (!ni.GetIPProperties().UnicastAddresses.Any(u => u.Address.ToString() == localIp)) continue;

                var info = new RouteInfo { Name = ni.Name, Description = ni.Description, Type = ni.NetworkInterfaceType.ToString() };
                string s = (ni.Name + " " + ni.Description).ToLowerInvariant();
                if (AdapterIgnore.Any(s.Contains)) return info;

                // Тип 53 (проприетарный виртуальный) и 131 (Tunnel) — так отдают себя Wintun и TAP-адаптеры VPN-клиентов
                int type = (int)ni.NetworkInterfaceType;
                bool byType = type == 53 || type == 131;
                bool byName = AdapterHints.Any(s.Contains) || Regex.IsMatch(s, @"\b(tun|tap)\d*\b");
                if (!byType && !byName) info.Hardware = IsHardwareAdapter(ni);
                info.IsVpn = byType || byName || info.Hardware == false;
                return info;
            }
        }
        catch { }
        return null;
    }

    static bool? IsHardwareAdapter(NetworkInterface ni)
    {
        lock (HardwareCache)
        {
            if (HardwareCache.TryGetValue(ni.Id, out var cached)) return cached;
        }
        bool? result = null;
        var r = PowerShell("(Get-NetAdapter -Name '" + ni.Name.Replace("'", "''") + "').HardwareInterface", 20000);
        if (r.Code == 0)
        {
            string o = r.Output.Trim();
            if (o.StartsWith("True", StringComparison.OrdinalIgnoreCase)) result = true;
            else if (o.StartsWith("False", StringComparison.OrdinalIgnoreCase)) result = false;
        }
        lock (HardwareCache) HardwareCache[ni.Id] = result;
        return result;
    }

    static string RemoveTaskScript(string name)
        => "if (Get-ScheduledTask -TaskName '" + name + "' -ErrorAction SilentlyContinue) { Unregister-ScheduledTask -TaskName '" + name + "' -Confirm:$false }; ";

    /// <summary>Возвращает текст ошибки или null. Явный exit 0 в конце обязателен: иначе код возврата берётся из $? последней команды,
    /// а Get-ScheduledTask для несуществующей задачи оставляет $? = false даже при SilentlyContinue.</summary>
    public static string SetAutostart(bool on)
    {
        string script = RemoveTaskScript(LegacyTaskName);
        if (on)
        {
            string exe = (Environment.ProcessPath ?? "").Replace("'", "''");
            script +=
                "$u = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name; " +
                "$a = New-ScheduledTaskAction -Execute '" + exe + "' -Argument '--autostart'; " +
                "$t = New-ScheduledTaskTrigger -AtLogOn -User $u; " +
                "$p = New-ScheduledTaskPrincipal -UserId $u -LogonType Interactive -RunLevel Highest; " +
                "$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero); " +
                "Register-ScheduledTask -TaskName '" + TaskName + "' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null; ";
        }
        else
        {
            script += RemoveTaskScript(TaskName);
        }
        var r = PowerShell(script + "exit 0", 40000);
        return r.Code == 0 ? null : r.Output.Trim();
    }

    /// <summary>Задача автозапуска могла остаться от прежней версии (другой путь к exe, старое имя) — пересоздаёт её, если она не указывает на текущий exe.</summary>
    public static string EnsureAutostart()
    {
        string exe = (Environment.ProcessPath ?? "").Replace("'", "''");
        var r = PowerShell(
            "$t = Get-ScheduledTask -TaskName '" + TaskName + "' -ErrorAction SilentlyContinue; " +
            "$l = Get-ScheduledTask -TaskName '" + LegacyTaskName + "' -ErrorAction SilentlyContinue; " +
            "if ($t -and -not $l -and $t.Actions[0].Execute -eq '" + exe + "') { 'current' }; exit 0");
        return r.Code == 0 && r.Output.Contains("current") ? null : SetAutostart(true);
    }
}
