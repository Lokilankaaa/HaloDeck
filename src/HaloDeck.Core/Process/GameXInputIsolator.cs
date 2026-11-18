using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HaloDeck.Core.Analysis;
using HaloDeck.Core.Models;

namespace HaloDeck.Core.Process;

/// <summary>
/// Mutes XInput polling (<c>XInputEnable(FALSE)</c>) inside unfocused 64-bit non-anticheat game processes
/// while HaloDeck's overlay/console is open, preventing background-polling engines (e.g. Koei Tecmo Katana Engine / Nioh 3)
/// from reacting to controller buttons while navigating HaloDeck, without triggering controller-disconnect popups.
/// </summary>
public sealed class GameXInputIsolator : IDisposable
{
    private const uint PROCESS_CREATE_THREAD = 0x0002;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint RequiredProcessAccess =
        PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE;

    private const uint LIST_MODULES_64BIT = 0x02;

    private static readonly HashSet<string> IgnoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "HaloDeck",
        "HaloDeck.App",
        "HandheldOpti",
        "GPDTool",
        "GPDKeyboard",
        "GPDKeyboardTools",
        "explorer",
        "ShellExperienceHost",
        "SearchHost",
        "StartMenuExperienceHost",
        "TextInputHost",
        "TabTip",
        "ApplicationFrameHost",
        "SystemSettings",
        "dwm",
        "csrss",
        "winlogon",
        "svchost",
        "sihost",
        "ctfmon",
        "LockApp",
        "Taskmgr",
        "GameBar",
        "GameBarFTServer",
        "steam",
        "steamwebhelper",
        "gameoverlayui",
        "gameoverlayui64",
        "EpicGamesLauncher",
        "EpicWebHelper",
        "GalaxyClient",
        "XboxApp",
        "GamingServices",
        "Playnite.FullscreenApp",
        "Playnite.DesktopApp",
        "msedge",
        "msedgewebview2",
        "chrome",
        "firefox",
        "Code",
        "devenv",
        "Antigravity",
        "powershell",
        "pwsh",
        "cmd",
        "WindowsTerminal",
        "conhost",
        "python",
        "pythonw",
        "ffmpeg",
        "ffprobe"
    };

    private static readonly string[] AntiCheatModuleTokens =
    {
        "EasyAntiCheat",
        "BattlEye",
        "BEClient",
        "BEService",
        "SGuard",
        "AntiCheatExpert",
        "EAAntiCheat",
        "vgk",
        "randgrid",
        "GameGuard",
        "nProtect",
        "xigncode",
        "DenuvoAntiCheat"
    };

    private static readonly HashSet<string> SupportedXInputDlls = new(StringComparer.OrdinalIgnoreCase)
    {
        "xinput1_4.dll",
        "xinput1_3.dll"
    };

    private static readonly ConcurrentDictionary<string, nuint> ExportRvaCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, bool> SafeDirectoryCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _sync = new();
    private readonly HashSet<int> _mutedPids = new();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(nint hProcess, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32EnumProcessModulesEx(
        nint hProcess,
        [Out] nint[] lphModule,
        uint cb,
        out uint lpcbNeeded,
        uint dwFilterFlag);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint K32GetModuleBaseNameW(
        nint hProcess,
        nint hModule,
        System.Text.StringBuilder lpBaseName,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint K32GetModuleFileNameExW(
        nint hProcess,
        nint hModule,
        System.Text.StringBuilder lpFilename,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint LoadLibraryW(string lpLibFileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern nint GetProcAddress(nint hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateRemoteThread(
        nint hProcess,
        nint lpThreadAttributes,
        nuint dwStackSize,
        nint lpStartAddress,
        nint lpParameter,
        uint dwCreationFlags,
        out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    public bool HasMutedProcesses
    {
        get
        {
            lock (_sync)
            {
                return _mutedPids.Count > 0;
            }
        }
    }

    public bool TryMuteProcess(int pid, bool knownHighRiskAntiCheat = false)
    {
        if (pid <= 4 || pid == Environment.ProcessId || knownHighRiskAntiCheat)
        {
            return false;
        }

        lock (_sync)
        {
            if (_mutedPids.Contains(pid))
            {
                return true;
            }
        }

        if (!TrySetProcessXInputEnabled(pid, enabled: false))
        {
            return false;
        }

        lock (_sync)
        {
            _mutedPids.Add(pid);
        }
        return true;
    }

    public void RestoreAll()
    {
        int[] pids;
        lock (_sync)
        {
            if (_mutedPids.Count == 0)
            {
                return;
            }
            pids = _mutedPids.ToArray();
            _mutedPids.Clear();
        }

        foreach (int pid in pids)
        {
            try
            {
                TrySetProcessXInputEnabled(pid, enabled: true);
            }
            catch
            {
                // Ignore if game already exited
            }
        }
    }

    public void Dispose() => RestoreAll();

    public static bool TrySetProcessXInputEnabled(int pid, bool enabled, bool allowSelfForTest = false)
    {
        if (!Environment.Is64BitProcess || pid <= 4)
        {
            return false;
        }

        if (!allowSelfForTest && pid == Environment.ProcessId)
        {
            return false;
        }

        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(pid);
            if (proc.HasExited)
            {
                return false;
            }

            if (!allowSelfForTest && IgnoredProcessNames.Contains(proc.ProcessName))
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        nint hProc = OpenProcess(RequiredProcessAccess, false, (uint)pid);
        if (hProc == 0)
        {
            return false;
        }

        try
        {
            if (!IsWow64Process(hProc, out bool isWow64) || isWow64)
            {
                return false;
            }

            var modBuf = new nint[1024];
            if (!K32EnumProcessModulesEx(hProc, modBuf, (uint)(modBuf.Length * nint.Size), out uint cbNeeded, LIST_MODULES_64BIT) || cbNeeded == 0)
            {
                return false;
            }

            int modCount = (int)(cbNeeded / (uint)nint.Size);
            if (modCount > modBuf.Length)
            {
                modBuf = new nint[modCount];
                if (!K32EnumProcessModulesEx(hProc, modBuf, (uint)(modBuf.Length * nint.Size), out cbNeeded, LIST_MODULES_64BIT))
                {
                    return false;
                }
                modCount = Math.Min(modBuf.Length, (int)(cbNeeded / (uint)nint.Size));
            }

            var nameSb = new System.Text.StringBuilder(260);
            var pathSb = new System.Text.StringBuilder(1024);
            var xinputTargets = new List<(string ModName, string ModPath, nint ModBase)>();

            // First module is the main executable image
            if (!enabled && !allowSelfForTest && modCount > 0 && modBuf[0] != 0)
            {
                pathSb.Clear();
                if (K32GetModuleFileNameExW(hProc, modBuf[0], pathSb, pathSb.Capacity) > 0)
                {
                    var exeDir = Path.GetDirectoryName(pathSb.ToString());
                    if (!string.IsNullOrWhiteSpace(exeDir) && !IsDirectoryAntiCheatSafe(exeDir))
                    {
                        return false;
                    }
                }
            }

            for (int i = 0; i < modCount; i++)
            {
                nint modBase = modBuf[i];
                if (modBase == 0) continue;

                nameSb.Clear();
                if (K32GetModuleBaseNameW(hProc, modBase, nameSb, nameSb.Capacity) == 0)
                {
                    continue;
                }

                string modName = nameSb.ToString();
                if (!enabled && !allowSelfForTest && IsAntiCheatModuleName(modName))
                {
                    return false;
                }

                if (SupportedXInputDlls.Contains(modName))
                {
                    pathSb.Clear();
                    string modPath = K32GetModuleFileNameExW(hProc, modBase, pathSb, pathSb.Capacity) > 0
                        ? pathSb.ToString()
                        : modName;
                    xinputTargets.Add((modName, modPath, modBase));
                }
            }

            if (xinputTargets.Count == 0)
            {
                return false;
            }

            bool anyApplied = false;
            foreach (var (modName, modPath, remoteBase) in xinputTargets)
            {
                nuint rva = ResolveXInputEnableRva(modPath, modName);
                if (rva == 0)
                {
                    continue;
                }

                nint remoteFn = (nint)((nuint)remoteBase + rva);
                nint hThread = CreateRemoteThread(
                    hProc,
                    0,
                    0,
                    remoteFn,
                    enabled ? new nint(1) : 0,
                    0,
                    out _);

                if (hThread != 0)
                {
                    try
                    {
                        WaitForSingleObject(hThread, 100);
                        anyApplied = true;
                    }
                    finally
                    {
                        CloseHandle(hThread);
                    }
                }
            }

            return anyApplied;
        }
        catch
        {
            return false;
        }
        finally
        {
            CloseHandle(hProc);
        }
    }

    public static bool IsAntiCheatModuleName(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return false;
        }

        foreach (var token in AntiCheatModuleTokens)
        {
            if (moduleName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDirectoryAntiCheatSafe(string exeDir)
    {
        return SafeDirectoryCache.GetOrAdd(exeDir, static dir =>
        {
            try
            {
                var ac = AntiCheatDetector.Detect(dir);
                return ac.RiskLevel == AntiCheatRiskLevel.None;
            }
            catch
            {
                return true;
            }
        });
    }

    private static nuint ResolveXInputEnableRva(string modulePath, string moduleName)
    {
        string key = !string.IsNullOrWhiteSpace(modulePath) ? modulePath : moduleName;
        return ExportRvaCache.GetOrAdd(key, _ =>
        {
            nint localMod = 0;
            if (!string.IsNullOrWhiteSpace(modulePath) && File.Exists(modulePath))
            {
                localMod = LoadLibraryW(modulePath);
            }
            if (localMod == 0)
            {
                localMod = LoadLibraryW(moduleName);
            }
            if (localMod == 0)
            {
                return 0;
            }

            nint proc = GetProcAddress(localMod, "XInputEnable");
            if (proc == 0)
            {
                return 0;
            }

            return (nuint)proc - (nuint)localMod;
        });
    }
}
