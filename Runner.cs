using System.Diagnostics;

namespace ZapretControlApp;

/// <summary>Запускает winws.exe как дочерний процесс, привязанный к Job Object:
/// умрёт наша программа (в том числе аварийно) — умрёт и winws.</summary>
public class Runner
{
    readonly IntPtr _job = Native.CreateKillOnCloseJob();
    Process _proc;

    public event Action<string> Log;
    public event Action Stopped;   // winws завершился сам (не по нашей команде)

    public string CurrentStrategy { get; private set; }

    public bool IsRunning
    {
        get { try { return _proc != null && !_proc.HasExited; } catch { return false; } }
    }

    public int Pid
    {
        get { try { return IsRunning ? _proc.Id : 0; } catch { return 0; } }
    }

    public bool Start(string zapretDir, Strategy st, string gameMode, out string error)
    {
        error = null;
        if (IsRunning) Stop(false);

        string args = st.BuildArgs(zapretDir, gameMode, out string problem);
        if (problem != null) { error = problem; return false; }

        string exe = Path.Combine(zapretDir, "bin", "winws.exe");
        if (!File.Exists(exe)) { error = "Не найден файл " + exe; return false; }

        Strategy.EnsureUserLists(zapretDir);

        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = Path.Combine(zapretDir, "bin"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (s, e) => { if (e.Data != null) Log?.Invoke("winws: " + e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) Log?.Invoke("winws: " + e.Data); };
            p.Exited += (s, e) => { if (ReferenceEquals(s, _proc)) Stopped?.Invoke(); };
            _proc = p;
            p.Start();
            Native.Assign(_job, p.Handle);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _proc = null;
            return false;
        }

        // Если winws сразу падает — это почти всегда ошибка в аргументах или антивирус
        if (_proc.WaitForExit(1200))
        {
            int code = -1;
            try { code = _proc.ExitCode; } catch { }
            error = "winws.exe сразу завершился (код " + code + "). Подробности — в журнале. Частые причины: антивирус удалил WinDivert, папка с кириллицей/OneDrive, другой обход уже запущен.";
            _proc = null;
            return false;
        }

        CurrentStrategy = st.Name;
        return true;
    }

    /// <param name="cleanup">Убрать драйвер WinDivert после остановки (если никто другой его не использует).</param>
    public void Stop(bool cleanup = true)
    {
        var p = _proc;
        _proc = null;           // чтобы обработчик Exited понял, что остановка была намеренной
        if (p != null)
        {
            try { if (!p.HasExited) p.Kill(true); } catch { }
            try { p.WaitForExit(5000); } catch { }
            try { p.Dispose(); } catch { }
        }
        CurrentStrategy = null;
        if (cleanup) CleanupDriver();
    }

    public static void CleanupDriver()
    {
        try
        {
            if (!SystemTools.AnyWinwsRunning() && !SystemTools.ZapretServiceRunning())
                SystemTools.CleanupWinDivert();
        }
        catch { }
    }
}
