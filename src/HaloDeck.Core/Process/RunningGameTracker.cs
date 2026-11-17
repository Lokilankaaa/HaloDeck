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

    public GameEntry? Poll(IReadOnlyList<GameEntry> libraryGames)
    {
        var snapshot = _foregroundProbe();
        if (snapshot is not null && !IsIgnoredProcess(snapshot))
        {
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

        // Preserve last active game when user switches to QAM overlay or desktop
        if (_lastActiveGame is not null)
        {
            bool stillAlive = IsProcessRunning(_lastActivePid);
            _lastActiveGame.IsCurrentlyRunning = stillAlive;
            _lastActiveGame.RunningPid = stillAlive ? _lastActivePid : null;
            _libraryService.RefreshInjectionStatus(_lastActiveGame);
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

    private static bool IsProcessRunning(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
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
