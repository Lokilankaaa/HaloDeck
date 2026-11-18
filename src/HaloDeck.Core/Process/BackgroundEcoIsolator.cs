using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HaloDeck.Core.Process;

public sealed class IsolatedProcessRecord
{
    public int Pid { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public bool EcoQosApplied { get; init; }
    public bool CpuSetsApplied { get; init; }
}

/// <summary>
/// Safely applies Windows 11 EcoQoS (ProcessPowerThrottling) and soft tail/efficiency-core
/// CPU Set isolation (SetProcessDefaultCpuSets) to non-essential background processes
/// (launcher webhelpers, browsers, sync clients, desktop widgets) while a game runs.
/// Never opens or modifies the game process or critical OS/audio/voice processes.
/// </summary>
public sealed class BackgroundEcoIsolator : IDisposable
{
    private const uint ProcessSetInformation = 0x0200;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessSetLimitedInformation = 0x2000;

    private const int ProcessPowerThrottlingInfoClass = 4;
    private const uint PowerThrottlingVersion = 1;
    private const uint ExecutionSpeedFlag = 0x1;

    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle",
        "System",
        "Registry",
        "smss",
        "csrss",
        "wininit",
        "services",
        "lsass",
        "svchost",
        "fontdrvhost",
        "dwm",
        "audiodg",
        "explorer",
        "sihost",
        "taskhostw",
        "ctfmon",
        "TextInputHost",
        "TabTip",
        "osk",
        "HaloDeck",
        "HaloDeck.App",
        "HandheldOpti",
        "msedgewebview2", // Keep HaloDeck's own QAM/Console WebView2 responsive at full refresh rate
        "amdpmfservice",
        "AmdPpkgSvc",
        "AMDRSServ",
        "AMDRSSrcExt",
        "RadeonSoftware",
        "GPDTool",
        "GPDKeyboard",
        "GPDKeyboardTools",
        "GameOverlayUI",  // Keep Steam Shift+Tab overlay responsive
        "Discord",        // Preserve real-time voice chat latency
        "KOOK",
        "TeamSpeak3",
        "YY"
    };

    private static readonly HashSet<string> BackgroundClutterProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "steamwebhelper",
        "EpicGamesLauncher",
        "EpicWebHelper",
        "GalaxyClient",
        "GalaxyClient Helper",
        "EADesktop",
        "EABackgroundService",
        "upc",
        "UplayWebCore",
        "Battle.net",
        "XboxApp",
        "XboxPcApp",
        "XboxPcAppFT",
        "msedge",
        "chrome",
        "firefox",
        "brave",
        "opera",
        "vivaldi",
        "qqbrowser",
        "OneDrive",
        "Dropbox",
        "BaiduNetdisk",
        "quark",
        "Thunder",
        "Widgets",
        "WidgetService",
        "PhoneExperienceHost",
        "YourPhone",
        "CrossDeviceService",
        "SearchHost",
        "SearchApp",
        "LockApp",
        "Wallpaper32",
        "Wallpaper64",
        "ui32"
    };

    private readonly object _lock = new();
    private readonly Dictionary<int, IsolatedProcessRecord> _isolated = new();

    public int IsolatedCount
    {
        get
        {
            lock (_lock)
            {
                return _isolated.Count;
            }
        }
    }

    public static bool ShouldIsolateProcess(
        int candidatePid,
        string? processName,
        string? candidateExePath,
        int activeGamePid,
        string? activeGameInstallDir,
        string? activeGameRenderDir)
    {
        if (candidatePid <= 4 ||
            candidatePid == Environment.ProcessId ||
            (activeGamePid > 4 && candidatePid == activeGamePid))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        string trimmedName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

        if (ProtectedProcessNames.Contains(trimmedName))
        {
            return false;
        }

        // Never touch any helper executable living inside the running game's directory tree
        if (!string.IsNullOrWhiteSpace(candidateExePath))
        {
            if (IsUnderDirectory(candidateExePath, activeGameInstallDir) ||
                IsUnderDirectory(candidateExePath, activeGameRenderDir))
            {
                return false;
            }
        }

        return BackgroundClutterProcessNames.Contains(trimmedName);
    }

    public int ApplyPass(
        IReadOnlyList<uint> backgroundCpuSetIds,
        int activeGamePid = 0,
        string? activeGameInstallDir = null,
        string? activeGameRenderDir = null)
    {
        lock (_lock)
        {
            PruneExitedAndProtectActiveGameLocked(activeGamePid, activeGameInstallDir, activeGameRenderDir);

            uint[] cpuSets = backgroundCpuSetIds is { Count: > 0 }
                ? backgroundCpuSetIds.ToArray()
                : Array.Empty<uint>();

            System.Diagnostics.Process[] snapshot;
            try
            {
                snapshot = System.Diagnostics.Process.GetProcesses();
            }
            catch
            {
                return _isolated.Count;
            }

            foreach (var proc in snapshot)
            {
                using (proc)
                {
                    int pid;
                    string procName;
                    try
                    {
                        pid = proc.Id;
                        procName = proc.ProcessName;
                    }
                    catch
                    {
                        continue;
                    }

                    if (_isolated.ContainsKey(pid))
                    {
                        continue;
                    }

                    // Fast name check before opening any handle
                    if (!ShouldIsolateProcess(pid, procName, null, activeGamePid, null, null))
                    {
                        continue;
                    }

                    nint hProc = OpenTargetProcess(pid);
                    if (hProc == 0)
                    {
                        continue;
                    }

                    try
                    {
                        string? exePath = QueryExecutablePath(hProc);
                        if (!ShouldIsolateProcess(pid, procName, exePath, activeGamePid, activeGameInstallDir, activeGameRenderDir))
                        {
                            continue;
                        }

                        bool ecoOk = TrySetEcoQos(hProc, enable: true);
                        bool cpuSetOk = cpuSets.Length > 0 && SetProcessDefaultCpuSets(hProc, cpuSets, (uint)cpuSets.Length);

                        if (ecoOk || cpuSetOk)
                        {
                            _isolated[pid] = new IsolatedProcessRecord
                            {
                                Pid = pid,
                                ProcessName = procName,
                                EcoQosApplied = ecoOk,
                                CpuSetsApplied = cpuSetOk
                            };
                        }
                    }
                    finally
                    {
                        CloseHandle(hProc);
                    }
                }
            }

            return _isolated.Count;
        }
    }

    public int RestoreAll()
    {
        int restored = 0;
        lock (_lock)
        {
            foreach (var rec in _isolated.Values.ToList())
            {
                if (RestoreProcessLocked(rec))
                {
                    restored++;
                }
            }

            _isolated.Clear();
        }
        return restored;
    }

    public void Dispose()
    {
        RestoreAll();
    }

    private void PruneExitedAndProtectActiveGameLocked(
        int activeGamePid,
        string? activeGameInstallDir,
        string? activeGameRenderDir)
    {
        foreach (var kv in _isolated.ToList())
        {
            int pid = kv.Key;
            var rec = kv.Value;

            if (activeGamePid > 4 && pid == activeGamePid)
            {
                RestoreProcessLocked(rec);
                _isolated.Remove(pid);
                continue;
            }

            nint hProc = OpenTargetProcess(pid);
            if (hProc == 0)
            {
                _isolated.Remove(pid);
                continue;
            }

            try
            {
                if (!GetExitCodeProcess(hProc, out uint code) || code != 259u /* STILL_ACTIVE */)
                {
                    _isolated.Remove(pid);
                    continue;
                }

                string? exePath = QueryExecutablePath(hProc);
                if (!ShouldIsolateProcess(pid, rec.ProcessName, exePath, activeGamePid, activeGameInstallDir, activeGameRenderDir))
                {
                    if (rec.EcoQosApplied) TrySetEcoQos(hProc, enable: false);
                    if (rec.CpuSetsApplied) SetProcessDefaultCpuSets(hProc, null, 0);
                    _isolated.Remove(pid);
                }
            }
            finally
            {
                CloseHandle(hProc);
            }
        }
    }

    private static bool RestoreProcessLocked(IsolatedProcessRecord rec)
    {
        nint hProc = OpenTargetProcess(rec.Pid);
        if (hProc == 0)
        {
            return false;
        }

        try
        {
            if (!GetExitCodeProcess(hProc, out uint code) || code != 259u)
            {
                return false;
            }

            bool ok = false;
            if (rec.EcoQosApplied)
            {
                ok |= TrySetEcoQos(hProc, enable: false);
            }
            if (rec.CpuSetsApplied)
            {
                ok |= SetProcessDefaultCpuSets(hProc, null, 0);
            }
            return ok;
        }
        finally
        {
            CloseHandle(hProc);
        }
    }

    private static nint OpenTargetProcess(int pid)
    {
        nint h = OpenProcess(ProcessSetLimitedInformation | ProcessQueryLimitedInformation, false, pid);
        if (h == 0)
        {
            h = OpenProcess(ProcessSetInformation | ProcessQueryLimitedInformation, false, pid);
        }
        return h;
    }

    private static bool TrySetEcoQos(nint hProc, bool enable)
    {
        var state = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = PowerThrottlingVersion,
            ControlMask = enable ? ExecutionSpeedFlag : 0u,
            StateMask = enable ? ExecutionSpeedFlag : 0u
        };

        return SetProcessInformation(
            hProc,
            ProcessPowerThrottlingInfoClass,
            ref state,
            (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
    }

    private static string? QueryExecutablePath(nint hProc)
    {
        Span<char> buffer = stackalloc char[1024];
        uint size = (uint)buffer.Length;
        if (QueryFullProcessImageNameW(hProc, 0, ref MemoryMarshal.GetReference(buffer), ref size) && size > 0)
        {
            return new string(buffer[..(int)size]);
        }
        return null;
    }

    private static bool IsUnderDirectory(string filePath, string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        try
        {
            string fullFile = Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullDir = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullFile.StartsWith(fullDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fullFile, fullDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(nint hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(
        nint hProcess,
        uint dwFlags,
        ref char lpExeName,
        ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(
        nint hProcess,
        int ProcessInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE ProcessInformation,
        uint ProcessInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDefaultCpuSets(
        nint Process,
        [In] uint[]? CpuSetIds,
        uint CpuSetIdCount);
}
