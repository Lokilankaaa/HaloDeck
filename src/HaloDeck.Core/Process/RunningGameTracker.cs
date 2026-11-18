using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using HaloDeck.Core.Analysis;
using HaloDeck.Core.Discovery;
using HaloDeck.Core.Injection;
using HaloDeck.Core.Models;

namespace HaloDeck.Core.Process;

public sealed record ForegroundProcessSnapshot(
    int ProcessId,
    nint WindowHandle,
    string ProcessName,
    string ExecutablePath);

public sealed class RunningGameTracker
{
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
        "ApplicationFrameHost",
        "SystemSettings",
        "dwm",
        "csrss",
        "LockApp",
        "Taskmgr",
        "steam",
        "steamwebhelper",
        "GameOverlayUI",
        "EpicGamesLauncher",
        "EpicWebHelper",
        "GalaxyClient",
        "XboxApp",
        "GamingServices",
        "Playnite.FullscreenApp",
        "Playnite.DesktopApp",
        "msedge",
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

    private readonly Func<ForegroundProcessSnapshot?> _foregroundProbe;
    private readonly GameLibraryService _libraryService;
    private readonly InjectionEngine _injectionEngine;

    private GameEntry? _lastActiveGame;
    private int _lastActivePid;
    private DateTime _lastProcessScanUtc = DateTime.MinValue;

    public RunningGameTracker(
        Func<ForegroundProcessSnapshot?> foregroundProbe,
        GameLibraryService libraryService,
        InjectionEngine injectionEngine)
    {
        _foregroundProbe = foregroundProbe;
        _libraryService = libraryService;
        _injectionEngine = injectionEngine;
    }

    public GameEntry? LastActiveGame => _lastActiveGame;

    public GameEntry? Poll(IReadOnlyList<GameEntry> libraryGames, bool allowBackgroundScan = false)
    {
        var snapshot = _foregroundProbe();
        if (snapshot is not null && !IsIgnoredProcess(snapshot))
        {
            // Fast O(1) zero-I/O path when the active foreground game PID has not changed
            if (_lastActiveGame is { IsCurrentlyRunning: true } &&
                snapshot.ProcessId > 4 &&
                snapshot.ProcessId == _lastActivePid)
            {
                return _lastActiveGame;
            }

            var matched = MatchLibraryGame(snapshot.ExecutablePath, libraryGames);
            if (matched is not null)
            {
                matched.IsCurrentlyRunning = true;
                matched.RunningPid = snapshot.ProcessId;
                _libraryService.RefreshInjectionStatus(matched);
                _lastActiveGame = matched;
                _lastActivePid = snapshot.ProcessId;
                return _lastActiveGame;
            }

            // Treat unknown foreground native/game process outside Windows/ProgramFiles launchers as ad-hoc running game
            if (!IsSystemDirectoryPath(snapshot.ExecutablePath))
            {
                var dir = Path.GetDirectoryName(snapshot.ExecutablePath)!;
                var adHocId = "running:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ExecutablePath.ToLowerInvariant())))[..10];
                var adHoc = new GameEntry
                {
                    Id = adHocId,
                    Title = Path.GetFileNameWithoutExtension(snapshot.ExecutablePath),
                    Platform = GamePlatform.RunningProcess,
                    InstallDirectory = dir,
                    TargetRenderDirectory = dir,
                    MainExecutablePath = snapshot.ExecutablePath,
                    IsCurrentlyRunning = true,
                    RunningPid = snapshot.ProcessId
                };
                _libraryService.EnrichGame(adHoc);
                _lastActiveGame = adHoc;
                _lastActivePid = snapshot.ProcessId;
                return _lastActiveGame;
            }
        }

        // Preserve last active game when user switches to QAM overlay or desktop without touching disk; clear once exited
        if (_lastActiveGame is not null)
        {
            bool stillAlive = IsProcessRunning(_lastActivePid);
            _lastActiveGame.IsCurrentlyRunning = stillAlive;
            _lastActiveGame.RunningPid = stillAlive ? _lastActivePid : null;
            if (stillAlive)
            {
                return _lastActiveGame;
            }

            _lastActiveGame = null;
            _lastActivePid = 0;
        }

        // Fallback every 12s ONLY when HaloDeck UI is open: detect running library games launched while Console/Steam owns focus
        if (allowBackgroundScan && libraryGames.Count > 0 && (DateTime.UtcNow - _lastProcessScanUtc) >= TimeSpan.FromSeconds(12.0))
        {
            _lastProcessScanUtc = DateTime.UtcNow;
            var bgMatched = TryFindRunningLibraryGame(libraryGames, out int bgPid);
            if (bgMatched is not null && bgPid > 4)
            {
                bgMatched.IsCurrentlyRunning = true;
                bgMatched.RunningPid = bgPid;
                _libraryService.RefreshInjectionStatus(bgMatched);
                _lastActiveGame = bgMatched;
                _lastActivePid = bgPid;
                return _lastActiveGame;
            }
        }

        return _lastActiveGame;
    }

    public void SelectManualFallbackGame(GameEntry game)
    {
        _lastActiveGame = game;
        _lastActivePid = game.RunningPid ?? 0;
        _libraryService.RefreshInjectionStatus(_lastActiveGame);
    }

    public async Task<InjectionResult> InjectAndRestartAsync(
        GameEntry game,
        OptiScalerConfig config,
        string activeOptiScalerSlot,
        string presetId,
        IReadOnlyCollection<string>? disabledCustomDlls = null,
        bool allowAntiCheatOverride = false,
        CancellationToken ct = default)
    {
        int pid = game.RunningPid ?? (_lastActiveGame?.Id == game.Id ? _lastActivePid : 0);
        string? exeToLaunch = game.MainExecutablePath;
        string? launchUri = game.LaunchUri;
        string targetDir = game.TargetRenderDirectory;

        if (pid > 0 && IsProcessRunning(pid))
        {
            var closed = await CloseProcessGracefullyAsync(pid, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            if (!closed)
            {
                return new InjectionResult
                {
                    Success = false,
                    Message = $"无法关闭正在运行的 {game.Title} (PID {pid})，请手动退出游戏后重试。"
                };
            }

            await WaitForDirectoryFileUnlockAsync(targetDir, TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
        }

        var injectResult = await _injectionEngine.InjectAsync(
            game,
            config,
            activeOptiScalerSlot,
            presetId,
            disabledCustomDlls,
            allowAntiCheatOverride,
            ct).ConfigureAwait(false);

        if (!injectResult.Success)
        {
            return injectResult;
        }

        _libraryService.RefreshInjectionStatus(game);

        // Relaunch via platform URI if available (preserves Steam Overlay & Steam Input!), else direct executable
        try
        {
            if (!string.IsNullOrWhiteSpace(launchUri))
            {
                System.Diagnostics.Process.Start(new ProcessStartInfo
                {
                    FileName = launchUri,
                    UseShellExecute = true
                });
                injectResult.Message += "，已通过原启动器重新拉起游戏";
            }
            else if (!string.IsNullOrWhiteSpace(exeToLaunch) && File.Exists(exeToLaunch))
            {
                System.Diagnostics.Process.Start(new ProcessStartInfo
                {
                    FileName = exeToLaunch,
                    WorkingDirectory = Path.GetDirectoryName(exeToLaunch),
                    UseShellExecute = true
                });
                injectResult.Message += "，已重新启动游戏";
            }
        }
        catch (Exception ex)
        {
            injectResult.Warnings.Add($"注入已完成，但自动拉起游戏失败: {ex.Message}");
        }

        return injectResult;
    }

    private static GameEntry? MatchLibraryGame(string foregroundExePath, IReadOnlyList<GameEntry> libraryGames)
    {
        var fullExe = Path.GetFullPath(foregroundExePath);
        foreach (var game in libraryGames)
        {
            if (!string.IsNullOrWhiteSpace(game.MainExecutablePath) &&
                string.Equals(Path.GetFullPath(game.MainExecutablePath), fullExe, StringComparison.OrdinalIgnoreCase))
            {
                return game;
            }

            if (!string.IsNullOrWhiteSpace(game.InstallDirectory))
            {
                var installRoot = Path.GetFullPath(game.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (fullExe.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return game;
                }
            }
        }
        return null;
    }

    private static bool IsIgnoredProcess(ForegroundProcessSnapshot snap)
    {
        return IgnoredProcessNames.Contains(snap.ProcessName) ||
               string.IsNullOrWhiteSpace(snap.ExecutablePath);
    }

    private static bool IsSystemDirectoryPath(string exePath)
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(winDir) && exePath.StartsWith(winDir, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var lower = exePath.ToLowerInvariant();
        return lower.Contains(@"\windowsapps\microsoft.") ||
               lower.Contains(@"\appdata\local\programs\") ||
               lower.Contains(@"\microsoft\edge\");
    }

    private static GameEntry? TryFindRunningLibraryGame(IReadOnlyList<GameEntry> libraryGames, out int matchedPid)
    {
        matchedPid = 0;
        var candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in libraryGames)
        {
            if (!string.IsNullOrWhiteSpace(g.MainExecutablePath))
            {
                string stem = Path.GetFileNameWithoutExtension(g.MainExecutablePath);
                if (!string.IsNullOrWhiteSpace(stem) && !IgnoredProcessNames.Contains(stem))
                {
                    candidateNames.Add(stem);
                }
            }
        }

        if (candidateNames.Count == 0)
        {
            return null;
        }

        System.Diagnostics.Process[] procs;
        try
        {
            procs = System.Diagnostics.Process.GetProcesses();
        }
        catch
        {
            return null;
        }

        try
        {
            foreach (var p in procs)
            {
                try
                {
                    if (p.Id <= 4 || p.Id == Environment.ProcessId) continue;
                    string procName = p.ProcessName;
                    if (!candidateNames.Contains(procName)) continue;

                    string? fullExe = TryQueryProcessImagePath(p.Id);
                    if (string.IsNullOrWhiteSpace(fullExe)) continue;

                    var matched = MatchLibraryGame(fullExe, libraryGames);
                    if (matched is not null)
                    {
                        matchedPid = p.Id;
                        return matched;
                    }
                }
                catch
                {
                    // Ignore exited/inaccessible process
                }
            }
        }
        finally
        {
            foreach (var p in procs)
            {
                p.Dispose();
            }
        }

        return null;
    }

    private static string? TryQueryProcessImagePath(int pid)
    {
        const uint ProcessQueryLimitedInformation = 0x1000;
        nint h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (h == 0) return null;
        try
        {
            Span<char> buf = stackalloc char[1024];
            uint size = (uint)buf.Length;
            if (QueryFullProcessImageNameW(h, 0, ref System.Runtime.InteropServices.MemoryMarshal.GetReference(buf), ref size) && size > 0)
            {
                return new string(buf[..(int)size]);
            }
            return null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, ref char lpExeName, ref uint lpdwSize);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(nint hProcess, out uint lpExitCode);

    private static bool IsProcessRunning(int pid)
    {
        if (pid <= 4) return false;
        const uint ProcessQueryLimitedInformation = 0x1000;
        nint h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (h == 0) return false;
        try
        {
            return GetExitCodeProcess(h, out uint exitCode) && exitCode == 259u /* STILL_ACTIVE */;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    private static async Task<bool> CloseProcessGracefullyAsync(int pid, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(pid);
            if (proc.HasExited) return true;

            proc.CloseMainWindow();
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                ct.ThrowIfCancellationRequested();
                proc.Refresh();
                if (proc.HasExited) return true;
                await Task.Delay(150, ct).ConfigureAwait(false);
            }

            proc.Kill(entireProcessTree: true);
            await Task.WhenAny(proc.WaitForExitAsync(ct), Task.Delay(2500, ct)).ConfigureAwait(false);
            proc.Refresh();
            return proc.HasExited;
        }
        catch
        {
            return !IsProcessRunning(pid);
        }
    }

    private static async Task WaitForDirectoryFileUnlockAsync(string targetDir, TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            bool anyLocked = false;
            foreach (var dll in InjectionEngine.AutoProxyPriority)
            {
                var p = Path.Combine(targetDir, dll);
                if (!File.Exists(p)) continue;
                try
                {
                    using var fs = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                catch
                {
                    anyLocked = true;
                    break;
                }
            }

            if (!anyLocked) return;
            await Task.Delay(150, ct).ConfigureAwait(false);
        }
    }
}
