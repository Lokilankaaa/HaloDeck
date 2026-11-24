using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using HaloDeck.App.Native;
using HaloDeck.Core.Analysis;
using HaloDeck.Core.Components;
using HaloDeck.Core.Config;
using HaloDeck.Core.Discovery;
using HaloDeck.Core.Injection;
using HaloDeck.Core.Models;
using HaloDeck.Core.Process;
using HaloDeck.Core.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace HaloDeck.App.Bridge;

public sealed class WebUiBridge : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SettingsStore _settingsStore;
    private readonly ComponentManager _componentManager;
    private readonly InjectionEngine _injectionEngine;
    private readonly GameLibraryService _libraryService;
    private readonly RunningGameTracker _runningTracker;
    private readonly BackgroundAutoInjector _backgroundAutoInjector;
    private readonly InputHubService _inputHub;
    private readonly Dispatcher _dispatcher;
    private static readonly bool IsVerifyOrCaptureCliRun = Environment.GetCommandLineArgs()
        .Any(a => a.StartsWith("--verify-", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(a, "--capture-docs", StringComparison.OrdinalIgnoreCase));

    private readonly SystemControlService _systemControl;
    private readonly GpdControllerService _gpdController = new();
    private readonly HandheldToolboxService _toolbox = new();
    private readonly GameXInputIsolator _xinputIsolator = new();
    private readonly RtssSharedMemoryClient _rtssClient = new();
    private readonly PowerPeripheralOptimizer _powerOptimizer;
    private readonly BackgroundEcoIsolator _steadyEcoIsolator = new();
    private readonly object _steadyEcoLock = new();

    private readonly List<WebView2> _registeredViews = new();
    private Task<CoreWebView2Environment>? _sharedEnvTask;
    private AppSettings _settings;
    private IReadOnlyList<GameEntry> _cachedGames = Array.Empty<GameEntry>();
    private GameEntry? _runningGame;
    private string? _selectedInspectGameId;

    private bool? _lastObservedAcOnline;
    private string? _appliedProfileGameId;
    private int? _preProfileRefreshRateHz;
    private bool? _preProfileDynamicRefreshRate;
    private bool _steadyEcoActive;
    private SteadyEcoPowerOptions? _lastAppliedSteadyPowerOpts;
    private DateTime _lastSteadyGameSeenUtc = DateTime.MinValue;
    private DateTime _lastBackgroundIsolatePassUtc = DateTime.MinValue;
    private int _lastSteadyIsolatedGamePid;
    private DateTime _lastFanCurvePushUtc = DateTime.MinValue;
    private int _lastFanCurveTempC = -999;
    private SystemControlState? _latestSystemState;
    private readonly SemaphoreSlim _tdpFastGate = new(1, 1);
    private readonly SemaphoreSlim _brightFastGate = new(1, 1);
    private readonly SemaphoreSlim _volFastGate = new(1, 1);
    private readonly object _saveLock = new();
    private CancellationTokenSource? _saveCts;

    public event Action? RequestHideQam;
    public event Action? RequestToggleQam;
    public event Action? RequestShowConsole;
    public event Action? RequestToggleFullscreen;
    public event Action? RequestMinimizeToTray;
    public event Action? RequestRepositionQam;
    public event Action? RequestExitForRelaunch;
    public event Action? OverlayHudUpdated;

    public void NotifyToast(string kind, string message)
        => BroadcastEvent("toast", new { kind, message });

    public WebUiBridge(
        SettingsStore settingsStore,
        ComponentManager componentManager,
        InjectionEngine injectionEngine,
        GameLibraryService libraryService,
        RunningGameTracker runningTracker,
        BackgroundAutoInjector backgroundAutoInjector,
        InputHubService inputHub,
        Dispatcher dispatcher)
    {
        _settingsStore = settingsStore;
        _componentManager = componentManager;
        _injectionEngine = injectionEngine;
        _libraryService = libraryService;
        _runningTracker = runningTracker;
        _backgroundAutoInjector = backgroundAutoInjector;
        _inputHub = inputHub;
        _dispatcher = dispatcher;
        _settings = _settingsStore.Load();
        _settings.UiTheme = string.Equals(_settings.UiTheme, "frost", StringComparison.OrdinalIgnoreCase) ? "frost" : "aurora";
        _settings.UiLayout = string.Equals(_settings.UiLayout, "cockpit", StringComparison.OrdinalIgnoreCase) ? "cockpit" : "bento";
        if (IsVerifyOrCaptureCliRun)
        {
            string verifyBackup = Path.Combine(Path.GetTempPath(), $"halodeck_verify_steady_{Environment.ProcessId}.json");
            _powerOptimizer = new PowerPeripheralOptimizer(new VerifyMemoryPowerBackend(), verifyBackup);
        }
        else
        {
            _powerOptimizer = new PowerPeripheralOptimizer();
            // Always clean up any stale backup and ensure SteadyEco / CPU cap are rolled back to clean OS defaults
            _powerOptimizer.RecoverIfStaleBackupExists(0);
            if (_settings.CpuMaxFreqMhz != 0 || !string.Equals(_settings.SteadyEcoMode, "off", StringComparison.OrdinalIgnoreCase))
            {
                _settings.CpuMaxFreqMhz = 0;
                _settings.SteadyEcoMode = "off";
                _settingsStore.Save(_settings);
            }
        }
        _systemControl = new SystemControlService(_powerOptimizer);

        _inputHub.GamepadCountChanged += count =>
            BroadcastEvent("gamepadStatus", new { connectedCount = count });

        _inputHub.UiGamepadAction += action =>
            BroadcastEvent("gamepadAction", new { action, target = _inputHub.ActiveUiTarget });

        _inputHub.GamepadComboRecorded += buttons =>
            BroadcastEvent("gamepadComboRecorded", new { buttons });

        _inputHub.BackButtonGamepadRecorded += codes =>
            BroadcastEvent("backButtonGamepadRecorded", new { codes });
    }

    public AppSettings Settings => _settings;
    public IReadOnlyList<GameEntry> CachedGames => _cachedGames;
    public GameEntry? RunningGame => _runningGame;
    public RtssSharedMemoryClient RtssClient => _rtssClient;
    public string ActiveOverlayBackend { get; set; } = RtssOsdFormatter.ModeNative;
    public SystemControlState LatestSystemState => _latestSystemState ?? (_latestSystemState = EnrichSystemState(_systemControl.GetState()));
    public int ResumeAllFrozenGames() => _toolbox.ResumeAllFrozenProcesses();
    public bool SuppressHardwareCleanupOnDispose { get; set; }

    public void MuteActiveGameControllerInput(nint externalForegroundHwnd)
    {
        if (!_settings.MuteGameGamepadInOverlay)
        {
            if (_xinputIsolator.HasMutedProcesses)
            {
                _xinputIsolator.RestoreAll();
            }
            return;
        }

        if (_runningGame is { IsCurrentlyRunning: true, RunningPid: > 4 and int runPid })
        {
            bool highRisk = _runningGame.AntiCheat?.RiskLevel == AntiCheatRiskLevel.HighRisk;
            _xinputIsolator.TryMuteProcess(runPid, knownHighRiskAntiCheat: highRisk);
        }

        if (externalForegroundHwnd != 0 && Win32Interop.IsWindow(externalForegroundHwnd))
        {
            Win32Interop.GetWindowThreadProcessId(externalForegroundHwnd, out uint fgPid);
            if (fgPid > 4 && fgPid != (uint)Environment.ProcessId)
            {
                bool highRisk = _runningGame is { RunningPid: var rp } && rp == (int)fgPid &&
                                _runningGame.AntiCheat?.RiskLevel == AntiCheatRiskLevel.HighRisk;
                _xinputIsolator.TryMuteProcess((int)fgPid, knownHighRiskAntiCheat: highRisk);
            }
        }
    }

    public void RestoreMutedGameControllerInput()
    {
        _xinputIsolator.RestoreAll();
    }

    public void Dispose()
    {
        _rtssClient.Dispose();
        _xinputIsolator.Dispose();
        FlushPendingSettingsSave();
        try
        {
            if (!SuppressHardwareCleanupOnDispose || _steadyEcoActive)
            {
                _steadyEcoIsolator.Dispose();
                _powerOptimizer.Dispose();
            }

            if (!SuppressHardwareCleanupOnDispose && _settings.FanMode != "auto")
            {
                var st = _systemControl.HardwareClient.GetStatus();
                if (st.ServiceRunning && st.EcReady)
                {
                    _systemControl.HardwareClient.SetFan("auto", _settings.ManualFanPercent, 48);
                }
            }
        }
        catch
        {
            // Ignore shutdown restoration failures
        }
        _toolbox.Dispose();
        _systemControl.Dispose();
    }

    public async Task InitializeAsync()
    {
        _cachedGames = await _libraryService.ScanAllAsync(_settings.ExcludedGameIds).ConfigureAwait(false);
        _runningGame = _runningTracker.Poll(_cachedGames);
        if (_settings.LaunchAtWindowsStartup && _settings.RunAsAdministrator && Win32Interop.IsCurrentProcessElevated())
        {
            ApplyWindowsStartup(enabled: true, runAsAdmin: true);
        }
        _ = RefreshComponentsCatalogSilentAsync();
        _ = SyncBackButtonHardwareSilentAsync();
    }

    private async Task SyncBackButtonHardwareSilentAsync()
    {
        try
        {
            var cfg = await Task.Run(() => _gpdController.ReadConfig(forceRefresh: false)).ConfigureAwait(false);
            if (cfg.Connected)
            {
                _inputHub.UpdateAssignedBackButtonFnCodes(CollectAssignedBackButtonFnCodes(cfg));
            }
        }
        catch
        {
            // Non-GPD systems or HID busy during startup; ignore cleanly
        }
    }

    public async Task AttachWebViewAsync(WebView2 webView, string mode)
    {
        if (webView.CoreWebView2 is null)
        {
            var userDataFolder = Path.Combine(
                HaloDeckPaths.ResolveAppDataRoot(),
                "WebView2");
            Directory.CreateDirectory(userDataFolder);

            _sharedEnvTask ??= CoreWebView2Environment.CreateAsync(null, userDataFolder);
            var env = await _sharedEnvTask;
            await webView.EnsureCoreWebView2Async(env);
        }

        var core = webView.CoreWebView2!;
        var webUiDir = Path.Combine(AppContext.BaseDirectory, "WebUI");
        core.SetVirtualHostNameToFolderMapping(
            "app.halodeck.local",
            webUiDir,
            CoreWebView2HostResourceAccessKind.Allow);

        var steamLibraryCache = SteamLibraryScanner.ResolveSteamLibraryCacheDir();
        if (!string.IsNullOrWhiteSpace(steamLibraryCache) && Directory.Exists(steamLibraryCache))
        {
            core.SetVirtualHostNameToFolderMapping(
                "steamcache.halodeck.local",
                steamLibraryCache,
                CoreWebView2HostResourceAccessKind.Allow);
        }

        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.IsStatusBarEnabled = false;

        lock (_registeredViews)
        {
            if (!_registeredViews.Contains(webView))
            {
                _registeredViews.Add(webView);
            }
        }

        core.WebMessageReceived += async (_, args) =>
        {
            await HandleWebMessageAsync(webView, args.WebMessageAsJson);
        };

        core.Navigate($"https://app.halodeck.local/index.html?mode={mode}");
    }

    private GameEntry? ResolveInspectGame()
    {
        if (!string.IsNullOrWhiteSpace(_selectedInspectGameId))
        {
            if (_runningGame is not null &&
                string.Equals(_runningGame.Id, _selectedInspectGameId, StringComparison.OrdinalIgnoreCase))
            {
                return _runningGame;
            }

            var selected = _cachedGames.FirstOrDefault(g =>
                string.Equals(g.Id, _selectedInspectGameId, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                return selected;
            }
        }

        return _runningGame ?? _cachedGames.FirstOrDefault();
    }

    public GameEntry? ActiveGameSnapshot => ResolveInspectGame();

    public void RefreshActiveGameForOverlay()
    {
        var current = _runningTracker.Poll(_cachedGames, allowBackgroundScan: true);
        if (current is { IsCurrentlyRunning: true })
        {
            _selectedInspectGameId = current.Id;
        }
        _runningGame = current;
        var target = ResolveInspectGame();
        if (target is not null)
        {
            _libraryService.RefreshInjectionStatus(target);
        }
        BroadcastStateSnapshot();
    }

    public void PollRunningAndAutoInjectTick(bool runAutoInjectPass, bool uiVisible = true, bool osdVisible = false)
    {
        var updatedRunning = _runningTracker.Poll(_cachedGames, allowBackgroundScan: uiVisible);
        bool changed = !Equals(updatedRunning?.Id, _runningGame?.Id) ||
                       updatedRunning?.IsCurrentlyRunning != _runningGame?.IsCurrentlyRunning ||
                       updatedRunning?.Injection.IsInjected != _runningGame?.Injection.IsInjected;
        if (updatedRunning is { IsCurrentlyRunning: true } &&
            !string.Equals(updatedRunning.Id, _runningGame?.Id, StringComparison.OrdinalIgnoreCase))
        {
            _selectedInspectGameId = updatedRunning.Id;
        }
        _runningGame = updatedRunning;

        var sys = EnrichSystemState(
            _systemControl.GetState(
                targetPid: ResolveTargetRenderPid(),
                uiVisible: uiVisible,
                osdVisible: osdVisible),
            allowWifiHardwareQuery: uiVisible);
        _latestSystemState = sys;

        // Fan heartbeat when daemon is online:
        // - Curve mode: push temperature every >=3.5s or >=2°C delta
        // - Manual mode: sync on startup/mode drift (e.g. after app restart) and refresh temp every >=5s so thermal floor never stays stale
        if (sys.HardwareService.ServiceRunning && sys.HardwareService.EcReady)
        {
            int peakTemp = Math.Max(sys.GpuTempC, sys.CpuTempC);
            if (_settings.FanMode is "curve" or "silent" or "balanced")
            {
                bool modeMismatch = !string.Equals(sys.HardwareService.FanMode, "curve", StringComparison.OrdinalIgnoreCase);
                if (peakTemp > 0 &&
                    (modeMismatch ||
                     (DateTime.UtcNow - _lastFanCurvePushUtc) >= TimeSpan.FromSeconds(3.5) ||
                     Math.Abs(peakTemp - _lastFanCurveTempC) >= 2))
                {
                    var activeCurve = FanCurveEvaluator.ResolveActiveCurve(_settings.FanCurves, _settings.ActiveFanCurveId);
                    _systemControl.HardwareClient.SetFan("curve", _settings.ManualFanPercent, peakTemp, activeCurve.Points);
                    _lastFanCurvePushUtc = DateTime.UtcNow;
                    _lastFanCurveTempC = peakTemp;
                }
            }
            else if (_settings.FanMode == "manual")
            {
                bool modeOrPctMismatch =
                    !string.Equals(sys.HardwareService.FanMode, "manual", StringComparison.OrdinalIgnoreCase) ||
                    sys.HardwareService.ManualFanPercent != _settings.ManualFanPercent;
                if (modeOrPctMismatch || (DateTime.UtcNow - _lastFanCurvePushUtc) >= TimeSpan.FromSeconds(5.0))
                {
                    int safeTemp = peakTemp > 0 ? peakTemp : 48;
                    _systemControl.HardwareClient.SetFan("manual", _settings.ManualFanPercent, safeTemp);
                    _lastFanCurvePushUtc = DateTime.UtcNow;
                    _lastFanCurveTempC = safeTemp;
                }
            }
        }

        // Handle per-game performance profile & AC/Battery power linkage transitions
        ApplyDynamicPowerAutomation(sys, updatedRunning);

        if (changed && uiVisible)
        {
            BroadcastStateSnapshot();
        }

        if (runAutoInjectPass && _settings.BackgroundAutoInjectEnabled)
        {
            _ = RunBackgroundAutoInjectAsync();
        }
    }

    private int ResolveTargetRenderPid()
    {
        if (_runningGame is { IsCurrentlyRunning: true, RunningPid: > 0 and var pid })
        {
            return pid;
        }
        return Win32Interop.GetForegroundProcessSnapshot()?.ProcessId ?? 0;
    }

    public SystemControlState PushSystemTelemetryTick()
    {
        var sys = EnrichSystemState(_systemControl.GetState(targetPid: ResolveTargetRenderPid()));
        _latestSystemState = sys;
        BroadcastEvent("systemTelemetry", sys);
        return sys;
    }

    public void BroadcastLatestTelemetry()
    {
        if (_latestSystemState is { } sys)
        {
            BroadcastEvent("systemTelemetry", sys);
        }
    }

    public void CycleOverlayHud()
    {
        _settings.OverlayLevel = (_settings.OverlayLevel + 1) % 5;
        _settingsStore.Save(_settings);
        OverlayHudUpdated?.Invoke();
        BroadcastStateSnapshot();
        BroadcastEvent("toast", new
        {
            kind = "info",
            message = _settings.OverlayLevel == 0
                ? "已关闭游戏内性能悬浮窗 (OSD)"
                : $"性能悬浮窗已切换至 Lv.{_settings.OverlayLevel}"
        });
    }

    public void ToggleGamepadMouseMode()
    {
        _settings.GamepadMouseModeEnabled = !_settings.GamepadMouseModeEnabled;
        _settingsStore.Save(_settings);
        _inputHub.GamepadMouseModeEnabled = _settings.GamepadMouseModeEnabled;
        BroadcastStateSnapshot();
        BroadcastEvent("toast", new
        {
            kind = "info",
            message = _settings.GamepadMouseModeEnabled
                ? "已开启手柄桌面鼠标模式（左摇杆移动 · A左键 · B右键 · 右摇杆滚轮）"
                : "已关闭手柄桌面鼠标模式"
        });
    }

    private void ApplyDynamicPowerAutomation(SystemControlState sys, GameEntry? running)
    {
        string? runningId = running is { IsCurrentlyRunning: true } ? running.Id : null;

        // 1. Per-Game Profile switch
        if (!string.Equals(runningId, _appliedProfileGameId, StringComparison.OrdinalIgnoreCase))
        {
            if (_appliedProfileGameId is not null && runningId is null)
            {
                // Exiting game: restore pre-game Hz (preserving DRR if it was active) and global/power-linked TDP
                bool restoreDyn = _preProfileDynamicRefreshRate ?? false;
                if (_preProfileRefreshRateHz is { } restoreHz &&
                    (sys.RefreshRateHz != restoreHz || sys.IsDynamicRefreshRate != restoreDyn))
                {
                    try { _systemControl.SetRefreshRate(restoreHz, restoreDyn); } catch { }
                }
                _preProfileRefreshRateHz = null;
                _preProfileDynamicRefreshRate = null;
                _appliedProfileGameId = null;

                int fallbackWatts = _settings.PowerLinkageEnabled
                    ? (sys.IsAcOnline ? _settings.AcTdpWatts : _settings.BatteryTdpWatts)
                    : _settings.CurrentTdpWatts;
                if (sys.HardwareService.ServiceRunning)
                {
                    int slowFallback = _settings.UnlockThreeStagePower ? _settings.SlowPptWatts : fallbackWatts;
                    int fastFallback = _settings.UnlockThreeStagePower
                        ? _settings.FastPptWatts
                        : (_settings.FlatPowerLock ? fallbackWatts : Math.Min(140, fallbackWatts + 2));
                    _systemControl.HardwareClient.SetTdp(fallbackWatts, slowFallback, fastFallback);
                }
            }
            else if (runningId is not null &&
                     _settings.PerGameProfiles.TryGetValue(runningId, out var prof) &&
                     prof.Enabled)
            {
                _preProfileRefreshRateHz ??= sys.RefreshRateHz;
                _preProfileDynamicRefreshRate ??= sys.IsDynamicRefreshRate;
                _appliedProfileGameId = runningId;
                if (prof.RefreshRateHz == -1 && sys.DynamicRefreshSupported && !sys.IsDynamicRefreshRate)
                {
                    try { _systemControl.SetRefreshRate(sys.DynamicCeilingHz > 0 ? sys.DynamicCeilingHz : 120, true); } catch { }
                }
                else if (prof.RefreshRateHz > 0 && (sys.RefreshRateHz != prof.RefreshRateHz || sys.IsDynamicRefreshRate))
                {
                    try { _systemControl.SetRefreshRate(prof.RefreshRateHz, false); } catch { }
                }
                if (sys.HardwareService.ServiceRunning && prof.TdpWatts is >= 8 and <= 120)
                {
                    int fastProf = _settings.FlatPowerLock ? prof.TdpWatts : Math.Min(140, prof.TdpWatts + 2);
                    _systemControl.HardwareClient.SetTdp(prof.TdpWatts, prof.TdpWatts, fastProf);
                }
                string hzTag = prof.RefreshRateHz == -1 ? " · 动态Hz" : (prof.RefreshRateHz > 0 ? $" · {prof.RefreshRateHz}Hz" : "");
                BroadcastEvent("toast", new
                {
                    kind = "info",
                    message = $"已应用《{running!.Title}》专属档：{prof.TdpWatts}W{hzTag}"
                });
            }
            else
            {
                _appliedProfileGameId = runningId;
            }
        }

        // 2. AC/Battery Power Linkage when not overridden by an active per-game TDP profile
        if (_lastObservedAcOnline != sys.IsAcOnline)
        {
            bool isInitial = _lastObservedAcOnline is null;
            _lastObservedAcOnline = sys.IsAcOnline;

            bool hasActiveGameTdp = _appliedProfileGameId is not null &&
                                    _settings.PerGameProfiles.TryGetValue(_appliedProfileGameId, out var gp) &&
                                    gp.Enabled;

            if (!isInitial && _settings.PowerLinkageEnabled && !hasActiveGameTdp)
            {
                int targetWatts = sys.IsAcOnline ? _settings.AcTdpWatts : _settings.BatteryTdpWatts;
                _settings.CurrentTdpWatts = targetWatts;
                _settingsStore.Save(_settings);
                if (sys.HardwareService.ServiceRunning)
                {
                    _systemControl.HardwareClient.SetTdp(targetWatts);
                }
                BroadcastEvent("toast", new
                {
                    kind = "info",
                    message = sys.IsAcOnline
                        ? $"已切换至外接电源模式 · TDP 自动调至 {targetWatts}W"
                        : $"已切换至电池续航模式 · TDP 自动调至 {targetWatts}W"
                });
                BroadcastStateSnapshot();
            }
        }
    }

    public async Task SendInsertKeyToGameAsync()
    {
        ushort vk = 0x2D; // VK_INSERT
        var running = _runningGame;
        if (running is not null && !string.IsNullOrWhiteSpace(running.TargetRenderDirectory))
        {
            var iniPath = Path.Combine(running.TargetRenderDirectory, "OptiScaler.ini");
            if (File.Exists(iniPath))
            {
                var ini = IniDocument.Load(iniPath);
                var shortcut = ini.Get("Menu", "ShortcutKey");
                if (!string.IsNullOrWhiteSpace(shortcut) &&
                    !string.Equals(shortcut, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    var trimmed = shortcut.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? shortcut[2..]
                        : shortcut;
                    if (ushort.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                    {
                        vk = parsed;
                    }
                }
            }
        }

        bool selfElevated = Win32Interop.IsCurrentProcessElevated();
        nint fgHwnd = Win32Interop.GetForegroundWindow();
        bool fgBlockedByUipi = !selfElevated && Win32Interop.IsWindowProcessElevated(fgHwnd);

        await Win32Interop.SendKeyHoldAsync(vk, _settings.InsertHoldDurationMs).ConfigureAwait(false);
        if (fgBlockedByUipi)
        {
            BroadcastEvent("toast", new
            {
                kind = "warn",
                message = $"前台游戏以管理员权限运行，Windows 安全隔离会拦截发送 (0x{vk:X2})；请在「手柄与设置」开启「以管理员权限运行」后重试（注意：联机反作弊游戏请勿注入 OptiScaler）"
            });
        }
        else
        {
            BroadcastEvent("toast", new { kind = "info", message = $"已向当前前台发送呼出键 (0x{vk:X2})" });
        }
    }

    private async Task HandleWebMessageAsync(WebView2 sender, string json)
    {
        string? requestId = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            requestId = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var method = root.TryGetProperty("method", out var mProp) ? mProp.GetString() : null;
            var payload = root.TryGetProperty("payload", out var pProp) ? pProp : default;

            if (string.IsNullOrWhiteSpace(method))
            {
                return;
            }

            object? result = method switch
            {
                "getSnapshot" => BuildSnapshot(),
                "scanGames" => await ScanGamesAsync(),
                "selectRunningFallback" => SelectRunningFallback(payload),
                "injectGame" => await InjectGameAsync(payload, restartRunning: false),
                "injectAndRestartGame" => await InjectGameAsync(payload, restartRunning: true),
                "restoreGame" => await RestoreGameAsync(payload),
                "launchGame" => LaunchGame(payload),
                "openFolder" => OpenFolder(payload),
                "addCustomGameByBrowse" => await AddCustomGameByBrowseAsync(payload),
                "addCustomGamePath" => AddCustomGamePath(payload),
                "removeCustomGame" => RemoveCustomGame(payload),
                "readGameConfigAndLog" => ReadGameConfigAndLog(payload),
                "refreshComponents" => await _componentManager.RefreshCatalogAsync(),
                "installComponent" => await InstallComponentAsync(payload),
                "importComponentFile" => await ImportComponentFileAsync(payload),
                "importCustomExtension" => await ImportCustomExtensionAsync(),
                "toggleCustomExtension" => ToggleCustomExtension(payload),
                "deleteCustomExtension" => DeleteCustomExtension(payload),
                "saveSettings" => SaveSettings(payload),
                "beginRecordGamepadCombo" => BeginRecordGamepadCombo(),
                "cancelRecordGamepadCombo" => CancelRecordGamepadCombo(),
                "beginRecordBackButton" => InvokeBeginRecordBackButton(),
                "cancelRecordBackButton" => InvokeCancelRecordBackButton(),
                "sendInsertKey" => await TriggerInsertFromUiAsync(),
                "hideQam" => InvokeUiAction(() => RequestHideQam?.Invoke()),
                "toggleQam" => InvokeUiAction(() => RequestToggleQam?.Invoke()),
                "showConsole" => InvokeUiAction(() => RequestShowConsole?.Invoke()),
                "toggleFullscreen" => InvokeUiAction(() => RequestToggleFullscreen?.Invoke()),
                "minimizeToTray" => InvokeUiAction(() => RequestMinimizeToTray?.Invoke()),
                "runAutoInjectNow" => await RunBackgroundAutoInjectAsync(),
                "expandPagefile" => await ExpandPagefileAsync(),
                "enableWindowedVrr" => EnableWindowedVrr(),
                "getSystemControl" => RefreshAndCacheSystemState(),
                "setRefreshRate" => ApplyRefreshRate(payload),
                "setResolution" => ApplyResolution(payload),
                "setBrightness" => await SetBrightnessFastAsync(payload),
                "setVolume" => await SetVolumeFastAsync(payload),
                "setMute" => BroadcastSystemState(_systemControl.SetMute(payload.GetProperty("muted").GetBoolean())),
                "setCpuBoost" => BroadcastSystemState(_systemControl.SetCpuBoostMode(payload.GetProperty("mode").GetString() ?? "aggressive")),
                "setCpuMaxFreq" => SetCpuMaxFreq(payload),
                "setTdp" => await SetTdpAsync(payload),
                "savePowerConfig" => SavePowerConfig(payload),
                "setPowerLinkage" => SetPowerLinkage(payload),
                "setFanMode" => SetFanMode(payload),
                "saveFanCurves" => SaveFanCurves(payload),
                "openSettingsJson" => OpenSettingsJson(),
                "reloadSettingsJson" => ReloadSettingsJson(),
                "setOverlayHud" => await SetOverlayHudAsync(payload),
                "ensureRtssReady" => await EnsureRtssReadyAsync(),
                "setGamepadMouseMode" => SetGamepadMouseMode(payload),
                "installHardwareService" => await InstallHardwareServiceAsync(),
                "toggleGpdService" => await ToggleGpdServiceAsync(payload),
                "runToolboxAction" => await RunToolboxActionAsync(payload),
                "getGpdController" => await ReadGpdControllerAsync(payload),
                "saveGpdController" => await SaveGpdControllerAsync(payload),
                "saveGamePerfProfile" => SaveGamePerfProfile(payload),
                "setSteadyEco" => await SetSteadyEcoAsync(payload),
                "toggleOfflineWifi" => await ToggleOfflineWifiAsync(payload),
                "setUiTheme" => SetUiTheme(payload),
                "setUiLayout" => SetUiLayout(payload),
                "openPawnIoDownload" => OpenPawnIoDownloadPage(),
                "relaunchAsAdmin" => RelaunchAsAdmin(payload),
                _ => throw new InvalidOperationException($"未知命令: {method}")
            };

            if (!string.IsNullOrWhiteSpace(requestId))
            {
                ReplyToSender(sender, requestId, ok: true, result: result, error: null);
            }
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                ReplyToSender(sender, requestId, ok: false, result: null, error: ex.Message);
            }
        }
    }

    private object BuildSnapshot()
    {
        var activeGame = ResolveInspectGame();
        var sysState = EnrichSystemState(_systemControl.GetState(targetPid: ResolveTargetRenderPid()));
        _latestSystemState = sysState;
        return new
        {
            runningGame = _runningGame,
            activeInspectGame = activeGame,
            games = _cachedGames,
            presets = PresetCatalog.GetPresets(),
            components = _componentManager.GetComponents(),
            customExtensions = _componentManager.GetCustomExtensions(_settings.DisabledCustomDlls),
            settings = _settings,
            settingsFilePath = _settingsStore.SettingsFilePath,
            proxyOptions = InjectionEngine.SupportedProxyNames,
            connectedControllers = _inputHub.ConnectedControllerCount,
            isElevated = Win32Interop.IsCurrentProcessElevated(),
            memoryHealth = Win32Interop.GetSystemMemoryHealth(),
            vrrWindowedEnabled = IsWindowedVrrEnabled(),
            systemControl = sysState,
            frozenGames = _toolbox.GetFrozenGames(),
            gpdController = _gpdController.CachedConfig,
            hardwareProfile = new
            {
                deviceName = "HaloDeck · GPD Win 5",
                recommendedArch = "AMD Strix Halo · RDNA 3.5",
                menuKeyHint = "L4/R4 可写 F13(侧栏) / F14(OSD) / Insert(插件)"
            }
        };
    }

    private static bool IsWindowedVrrEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", writable: false);
            var raw = key?.GetValue("DirectXUserGlobalSettings") as string;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            return raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(token => string.Equals(token, "VRROptimizeEnable=1", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private object EnableWindowedVrr()
    {
        const string subKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
        using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException("无法打开 DirectX 图形首选项注册表项。");

        var existingRaw = key.GetValue("DirectXUserGlobalSettings") as string ?? string.Empty;
        var preserved = existingRaw
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t =>
                !t.StartsWith("VRROptimizeEnable=", StringComparison.OrdinalIgnoreCase) &&
                !t.StartsWith("SwapEffectUpgradeEnable=", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var tokens = new List<string>
        {
            "VRROptimizeEnable=1",
            "SwapEffectUpgradeEnable=1"
        };
        tokens.AddRange(preserved);

        var updatedValue = string.Join(";", tokens) + ";";
        key.SetValue("DirectXUserGlobalSettings", updatedValue, RegistryValueKind.String);
        BroadcastStateSnapshot();

        return new
        {
            ok = true,
            vrrWindowedEnabled = true,
            message = "已开启 Win11 窗口化可变刷新率 (VRR) 与翻转模型优化，重开游戏生效。"
        };
    }

    private async Task<object> ExpandPagefileAsync()
    {
        const string script =
            "$path = 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management'; " +
            "$pf = \"$env:SystemDrive\\pagefile.sys 24576 40960\"; " +
            "Set-ItemProperty -Path $path -Name PagingFiles -Value ([string[]]@($pf))";
        string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("无法启动管理员 PowerShell 进程。");
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"写入系统分页文件注册表失败（退出码 {process.ExitCode}）。");
            }

            return new
            {
                ok = true,
                rebootRequired = true,
                message = "已将系统虚拟内存扩容为 24–40 GB，请重启掌机后生效。",
                memoryHealth = HaloDeck.App.Native.Win32Interop.GetSystemMemoryHealth()
            };
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("用户已取消管理员授权，系统虚拟内存设置未更改。");
        }
    }

    private async Task<object> ScanGamesAsync()
    {
        _cachedGames = await _libraryService.ScanAllAsync(_settings.ExcludedGameIds).ConfigureAwait(false);
        _runningGame = _runningTracker.Poll(_cachedGames);
        BroadcastStateSnapshot();
        return _cachedGames;
    }

    private object? SelectRunningFallback(JsonElement payload)
    {
        var gameId = payload.GetProperty("gameId").GetString();
        var game = _cachedGames.FirstOrDefault(g => string.Equals(g.Id, gameId, StringComparison.OrdinalIgnoreCase));
        if (game is not null)
        {
            _selectedInspectGameId = game.Id;
            _libraryService.RefreshInjectionStatus(game);
            BroadcastStateSnapshot();
            return game;
        }
        return ResolveInspectGame();
    }

    private async Task<InjectionResult> InjectGameAsync(JsonElement payload, bool restartRunning)
    {
        var gameId = payload.GetProperty("gameId").GetString() ?? "";
        var presetId = payload.TryGetProperty("presetId", out var p) ? p.GetString() ?? _settings.DefaultPresetId : _settings.DefaultPresetId;
        var allowAntiCheat = payload.TryGetProperty("allowAntiCheatOverride", out var ac) && ac.GetBoolean();

        OptiScalerConfig config;
        if (payload.TryGetProperty("config", out var cfgElem) && cfgElem.ValueKind == JsonValueKind.Object)
        {
            config = JsonSerializer.Deserialize<OptiScalerConfig>(cfgElem.GetRawText(), JsonOptions)
                     ?? PresetCatalog.GetById(presetId).Config;
        }
        else
        {
            config = PresetCatalog.GetById(presetId).Config;
        }

        var game = FindGameById(gameId)
                   ?? throw new InvalidOperationException("找不到目标游戏记录。");

        var result = restartRunning
            ? await _runningTracker.InjectAndRestartAsync(
                game,
                config,
                _settings.ActiveOptiScalerSlot,
                presetId,
                _settings.DisabledCustomDlls,
                allowAntiCheat).ConfigureAwait(false)
            : await _injectionEngine.InjectAsync(
                game,
                config,
                _settings.ActiveOptiScalerSlot,
                presetId,
                _settings.DisabledCustomDlls,
                allowAntiCheat).ConfigureAwait(false);

        _libraryService.RefreshInjectionStatus(game);
        if (_runningGame?.Id == game.Id)
        {
            _libraryService.RefreshInjectionStatus(_runningGame);
        }

        BroadcastStateSnapshot();
        return result;
    }

    private async Task<InjectionResult> RestoreGameAsync(JsonElement payload)
    {
        var gameId = payload.GetProperty("gameId").GetString() ?? "";
        var game = FindGameById(gameId)
                   ?? throw new InvalidOperationException("找不到目标游戏记录。");

        var result = await _injectionEngine.RestoreCleanAsync(game).ConfigureAwait(false);
        _libraryService.RefreshInjectionStatus(game);
        if (_runningGame?.Id == game.Id)
        {
            _libraryService.RefreshInjectionStatus(_runningGame);
        }

        BroadcastStateSnapshot();
        return result;
    }

    private object LaunchGame(JsonElement payload)
    {
        var gameId = payload.GetProperty("gameId").GetString() ?? "";
        var game = FindGameById(gameId) ?? throw new InvalidOperationException("找不到游戏。");

        if (!string.IsNullOrWhiteSpace(game.LaunchUri))
        {
            Process.Start(new ProcessStartInfo { FileName = game.LaunchUri, UseShellExecute = true });
        }
        else if (!string.IsNullOrWhiteSpace(game.MainExecutablePath) && File.Exists(game.MainExecutablePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = game.MainExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(game.MainExecutablePath),
                UseShellExecute = true
            });
        }
        else
        {
            throw new InvalidOperationException("没有可用的启动 URI 或可执行文件路径。");
        }
        return new { launched = true };
    }

    private object OpenFolder(JsonElement payload)
    {
        var path = payload.GetProperty("path").GetString();
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{path}\"", UseShellExecute = true });
        }
        return new { opened = true };
    }

    private async Task<object?> AddCustomGameByBrowseAsync(JsonElement payload)
    {
        var pickFolder = payload.TryGetProperty("pickFolder", out var pf) && pf.GetBoolean();
        string? selectedPath = null;

        await _dispatcher.InvokeAsync(() =>
        {
            if (pickFolder)
            {
                using var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "选择游戏安装根目录"
                };
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    selectedPath = dialog.SelectedPath;
                }
            }
            else
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择游戏主程序 (.exe)",
                    Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*"
                };
                if (dlg.ShowDialog() == true)
                {
                    selectedPath = dlg.FileName;
                }
            }
        });

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return null;
        }

        var entry = _libraryService.AddCustomGame(selectedPath);
        await ScanGamesAsync().ConfigureAwait(false);
        return entry;
    }

    private object AddCustomGamePath(JsonElement payload)
    {
        var path = payload.GetProperty("path").GetString() ?? "";
        var title = payload.TryGetProperty("title", out var t) ? t.GetString() : null;
        var entry = _libraryService.AddCustomGame(path, title);
        _cachedGames = _libraryService.ScanAllAsync(_settings.ExcludedGameIds).GetAwaiter().GetResult();
        BroadcastStateSnapshot();
        return entry;
    }

    private object RemoveCustomGame(JsonElement payload)
    {
        var gameId = payload.GetProperty("gameId").GetString() ?? "";
        _libraryService.RemoveCustomGame(gameId);
        _cachedGames = _libraryService.ScanAllAsync(_settings.ExcludedGameIds).GetAwaiter().GetResult();
        BroadcastStateSnapshot();
        return new { removed = true };
    }

    private object ReadGameConfigAndLog(JsonElement payload)
    {
        var gameId = payload.GetProperty("gameId").GetString() ?? "";
        var game = FindGameById(gameId) ?? throw new InvalidOperationException("未找到游戏");

        var manifest = _injectionEngine.ReadManifest(game.TargetRenderDirectory);
        var logSnippet = InjectionEngine.ReadOptiScalerLogSnippet(game.TargetRenderDirectory, 45);
        var iniPath = Path.Combine(game.TargetRenderDirectory, "OptiScaler.ini");
        string? iniContent = File.Exists(iniPath) ? File.ReadAllText(iniPath) : null;

        return new
        {
            manifest,
            logSnippet,
            iniExists = iniContent is not null,
            iniPreview = iniContent is not null && iniContent.Length > 4000 ? iniContent[..4000] : iniContent
        };
    }

    private async Task<ComponentInfo> InstallComponentAsync(JsonElement payload)
    {
        var slotStr = payload.GetProperty("slot").GetString() ?? "";
        if (!Enum.TryParse<ComponentSlot>(slotStr, out var slot))
        {
            throw new ArgumentException($"未知组件槽位: {slotStr}");
        }

        var progress = new Progress<double>(pct =>
        {
            BroadcastEvent("componentProgress", new { slot = slot.ToString(), percent = Math.Round(pct, 1) });
        });

        var info = await _componentManager.InstallOrUpdateAsync(slot, progress).ConfigureAwait(false);
        BroadcastStateSnapshot();
        return info;
    }

    private async Task<ComponentInfo?> ImportComponentFileAsync(JsonElement payload)
    {
        var slotStr = payload.GetProperty("slot").GetString() ?? "";
        if (!Enum.TryParse<ComponentSlot>(slotStr, out var slot))
        {
            throw new ArgumentException($"未知组件槽位: {slotStr}");
        }

        string? file = null;
        await _dispatcher.InvokeAsync(() =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"导入 {slot} 离线包或二进制文件",
                Filter = "组件压缩包或二进制 (*.7z;*.zip;*.dll;*.asi)|*.7z;*.zip;*.dll;*.asi|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                file = dlg.FileName;
            }
        });

        if (string.IsNullOrWhiteSpace(file))
        {
            return null;
        }

        var info = _componentManager.ImportLocalPackage(slot, file);
        BroadcastStateSnapshot();
        return info;
    }

    private async Task<CustomExtensionFile?> ImportCustomExtensionAsync()
    {
        string? file = null;
        await _dispatcher.InvokeAsync(() =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "导入附加扩展文件 (如 FSR 4 DLL / dlssg-to-fsr3 / 自定义 ASI)",
                Filter = "DLL / ASI / INI (*.dll;*.asi;*.ini)|*.dll;*.asi;*.ini|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                file = dlg.FileName;
            }
        });

        if (string.IsNullOrWhiteSpace(file))
        {
            return null;
        }

        var item = _componentManager.ImportCustomExtensionFile(file);
        BroadcastStateSnapshot();
        return item;
    }

    private object ToggleCustomExtension(JsonElement payload)
    {
        var fileName = payload.GetProperty("fileName").GetString() ?? "";
        var enabled = payload.GetProperty("enabled").GetBoolean();

        _settings.DisabledCustomDlls.RemoveAll(f => string.Equals(f, fileName, StringComparison.OrdinalIgnoreCase));
        if (!enabled)
        {
            _settings.DisabledCustomDlls.Add(fileName);
        }
        _settingsStore.Save(_settings);
        BroadcastStateSnapshot();
        return new { ok = true };
    }

    private object DeleteCustomExtension(JsonElement payload)
    {
        var fileName = payload.GetProperty("fileName").GetString() ?? "";
        _componentManager.DeleteCustomExtensionFile(fileName);
        _settings.DisabledCustomDlls.RemoveAll(f => string.Equals(f, fileName, StringComparison.OrdinalIgnoreCase));
        _settingsStore.Save(_settings);
        BroadcastStateSnapshot();
        return new { ok = true };
    }

    private AppSettings SaveSettings(JsonElement payload)
    {
        var updated = JsonSerializer.Deserialize<AppSettings>(payload.GetRawText(), JsonOptions)
                      ?? throw new ArgumentException("无效的设置格式");

        bool backHotkeyToggledOn = updated.EnableBackButtonHotkeys && !_settings.EnableBackButtonHotkeys;
        bool startupChanged = updated.LaunchAtWindowsStartup != _settings.LaunchAtWindowsStartup ||
                              updated.RunAsAdministrator != _settings.RunAsAdministrator;
        _settings = updated;
        _settings.BackButtonHotkeysSchemaVersion = 1;
        _settingsStore.Save(_settings);
        if (backHotkeyToggledOn && CollectAssignedBackButtonFnCodes(_gpdController.CachedConfig).Count == 0)
        {
            // User explicitly enabled F13-F16 hotkey takeover via UI switch
            _inputHub.UpdateAssignedBackButtonFnCodes(null);
        }
        _inputHub.UpdateSettings(_settings);

        if (!_settings.MuteGameGamepadInOverlay && _xinputIsolator.HasMutedProcesses)
        {
            _xinputIsolator.RestoreAll();
        }

        if (startupChanged)
        {
            ApplyWindowsStartup(_settings.LaunchAtWindowsStartup, _settings.RunAsAdministrator);
        }

        BroadcastStateSnapshot();
        return _settings;
    }

    private object RelaunchAsAdmin(JsonElement payload)
    {
        bool persistFlag = !payload.TryGetProperty("persist", out var pp) || pp.GetBoolean();
        if (Win32Interop.IsCurrentProcessElevated())
        {
            if (persistFlag && !_settings.RunAsAdministrator)
            {
                _settings.RunAsAdministrator = true;
                _settingsStore.Save(_settings);
                if (_settings.LaunchAtWindowsStartup)
                {
                    ApplyWindowsStartup(enabled: true, runAsAdmin: true);
                }
                BroadcastStateSnapshot();
            }
            return new { ok = true, alreadyElevated = true, message = "HaloDeck 当前已处于管理员权限模式" };
        }

        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            throw new InvalidOperationException("无法定位当前 HaloDeck 可执行文件路径。");
        }

        if (persistFlag)
        {
            _settings.RunAsAdministrator = true;
            _settingsStore.Save(_settings);
            BroadcastStateSnapshot();
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--relaunch-wait-pid {Environment.ProcessId} --no-auto-elevate",
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new { ok = false, cancelled = true, message = "已取消管理员提权授权，保持普通权限运行" };
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(120).ConfigureAwait(false);
            RequestExitForRelaunch?.Invoke();
        });

        return new { ok = true, relaunching = true, message = "正在以管理员权限重启 HaloDeck..." };
    }

    private static object OpenPawnIoDownloadPage()
    {
        const string url = "https://pawnio.eu/";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return new { ok = true, url };
    }

    private object BeginRecordGamepadCombo()
    {
        _inputHub.BeginRecordGamepadCombo();
        return new { recording = true };
    }

    private object CancelRecordGamepadCombo()
    {
        _inputHub.CancelRecordGamepadCombo();
        return new { recording = false };
    }

    private object InvokeBeginRecordBackButton()
    {
        _inputHub.BeginRecordBackButtonGamepad();
        return new { recording = true };
    }

    private object InvokeCancelRecordBackButton()
    {
        _inputHub.CancelRecordBackButtonGamepad();
        return new { recording = false };
    }

    private void ScheduleSaveSettings(int delayMs = 350)
    {
        CancellationTokenSource nextCts;
        lock (_saveLock)
        {
            _saveCts?.Cancel();
            _saveCts?.Dispose();
            nextCts = new CancellationTokenSource();
            _saveCts = nextCts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, nextCts.Token).ConfigureAwait(false);
                lock (_saveLock)
                {
                    if (!ReferenceEquals(_saveCts, nextCts)) return;
                    _settingsStore.Save(_settings);
                    _saveCts = null;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Best-effort debounced persistence
            }
        });
    }

    private void FlushPendingSettingsSave()
    {
        lock (_saveLock)
        {
            if (_saveCts is null) return;
            _saveCts.Cancel();
            _saveCts.Dispose();
            _saveCts = null;
            _settingsStore.Save(_settings);
        }
    }

    private async Task<object> TriggerInsertFromUiAsync()
    {
        await _dispatcher.InvokeAsync(() => RequestHideQam?.Invoke());
        await Task.Delay(120).ConfigureAwait(false);
        await SendInsertKeyToGameAsync().ConfigureAwait(false);
        return new { sent = true };
    }

    private async Task<object> RunBackgroundAutoInjectAsync()
    {
        var events = await _backgroundAutoInjector.RunPassAsync(_cachedGames, _settings).ConfigureAwait(false);
        if (events.Count > 0)
        {
            foreach (var game in _cachedGames)
            {
                _libraryService.RefreshInjectionStatus(game);
            }
            BroadcastStateSnapshot();
        }
        return events;
    }

    private async Task RefreshComponentsCatalogSilentAsync()
    {
        try
        {
            await _componentManager.RefreshCatalogAsync().ConfigureAwait(false);
            BroadcastStateSnapshot();
        }
        catch
        {
            // Offline tolerance
        }
    }

    private GameEntry? FindGameById(string gameId)
    {
        if (_runningGame is not null && string.Equals(_runningGame.Id, gameId, StringComparison.OrdinalIgnoreCase))
        {
            return _runningGame;
        }
        return _cachedGames.FirstOrDefault(g => string.Equals(g.Id, gameId, StringComparison.OrdinalIgnoreCase));
    }

    private object InvokeUiAction(Action action)
    {
        _dispatcher.Invoke(action);
        return new { ok = true };
    }

    private SystemControlState ApplyRefreshRate(JsonElement payload)
    {
        bool dynamic = payload.TryGetProperty("dynamic", out var dynProp) &&
                       dynProp.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                       dynProp.GetBoolean();
        int hz = payload.TryGetProperty("hz", out var hzProp) && hzProp.ValueKind == JsonValueKind.Number
            ? hzProp.GetInt32()
            : 120;
        var updated = _systemControl.SetRefreshRate(hz, dynamic);
        _latestSystemState = updated;
        _dispatcher.InvokeAsync(() => RequestRepositionQam?.Invoke());
        BroadcastStateSnapshot();
        return updated;
    }

    private SystemControlState ApplyResolution(JsonElement payload)
    {
        int width;
        int height;
        if (payload.TryGetProperty("resolution", out var resProp) &&
            resProp.GetString() is { } resStr &&
            resStr.Split('x', 'X') is { Length: 2 } parts &&
            int.TryParse(parts[0], out width) &&
            int.TryParse(parts[1], out height))
        {
            // Parsed from "WxH"
        }
        else
        {
            width = payload.GetProperty("width").GetInt32();
            height = payload.GetProperty("height").GetInt32();
        }

        var updated = _systemControl.SetResolution(width, height);
        _dispatcher.InvokeAsync(() => RequestRepositionQam?.Invoke());
        BroadcastStateSnapshot();
        return updated;
    }

    private SystemControlState BroadcastSystemState(SystemControlState updated)
    {
        EnrichSystemState(updated);
        _latestSystemState = updated;
        BroadcastEvent("systemTelemetry", updated);
        return updated;
    }

    private SystemControlState RefreshAndCacheSystemState()
    {
        var st = EnrichSystemState(_systemControl.GetState(includeHardwareRefresh: true));
        _latestSystemState = st;
        return st;
    }

    private SystemControlState EnrichSystemState(SystemControlState state, bool allowWifiHardwareQuery = true)
    {
        state.SteadyEcoActive = _steadyEcoActive;
        state.IsolatedProcessCount = _steadyEcoIsolator.IsolatedCount;
        var (wifiSupported, wifiOff) = _toolbox.GetWifiRadioStatus(forceRefresh: false, allowHardwareQuery: allowWifiHardwareQuery);
        state.WifiRadioSupported = wifiSupported;
        state.WifiRadioOff = wifiOff;
        state.RtssInstalled = _rtssClient.IsRtssInstalled();
        state.RtssRunning = _rtssClient.IsRtssRunning();
        state.ActiveOverlayBackend = ActiveOverlayBackend;
        return state;
    }

    private async Task<object> SetSteadyEcoAsync(JsonElement payload)
    {
        if (payload.TryGetProperty("mode", out var mProp) && mProp.ValueKind == JsonValueKind.String)
        {
            string rawMode = (mProp.GetString() ?? "off").Trim().ToLowerInvariant();
            _settings.SteadyEcoMode = rawMode is "auto" or "always" ? rawMode : "off";
        }

        if (payload.TryGetProperty("cpuEnabled", out var cpuProp) && cpuProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _settings.SteadyEcoCpuEnabled = cpuProp.GetBoolean();
        }

        if (payload.TryGetProperty("backgroundEnabled", out var bgProp) && bgProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _settings.SteadyEcoBackgroundEnabled = bgProp.GetBoolean();
        }

        if (payload.TryGetProperty("peripheralEnabled", out var perProp) && perProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _settings.SteadyEcoPeripheralEnabled = perProp.GetBoolean();
        }

        if (payload.TryGetProperty("cpuEpp", out var eppProp) && eppProp.ValueKind == JsonValueKind.Number)
        {
            _settings.SteadyEcoCpuEpp = Math.Clamp(eppProp.GetInt32(), 50, 90);
        }

        _settingsStore.Save(_settings);

        await Task.Run(() => UpdateSteadyEcoAutomation(_runningGame, allowAutoToast: false, forceImmediate: true)).ConfigureAwait(false);
        _systemControl.InvalidatePowerSchemeCache();

        var sys = EnrichSystemState(_systemControl.GetState(targetPid: ResolveTargetRenderPid()));
        _latestSystemState = sys;
        BroadcastStateSnapshot();

        string statusMsg = _settings.SteadyEcoMode switch
        {
            "always" => _steadyEcoActive
                ? $"已常驻启用稳帧净空模式（净空 {_steadyEcoIsolator.IsolatedCount} 个后台进程 · EPP {_settings.SteadyEcoCpuEpp}）"
                : "稳帧净空已设为常驻（请至少勾选一项子优化）",
            "auto" => _steadyEcoActive
                ? $"稳帧净空已随当前游戏生效（净空 {_steadyEcoIsolator.IsolatedCount} 个后台进程）"
                : "已开启「随游戏自动」：检测到游戏启动自动稳帧降耗，退游自动还原",
            _ => "已关闭稳帧净空模式，系统电源与后台调度已全部还原默认"
        };

        return new
        {
            ok = true,
            settings = _settings,
            systemControl = sys,
            steadyEcoActive = _steadyEcoActive,
            isolatedProcessCount = _steadyEcoIsolator.IsolatedCount,
            message = statusMsg
        };
    }

    private object SetUiTheme(JsonElement payload)
    {
        string raw = payload.TryGetProperty("theme", out var tp) ? (tp.GetString() ?? "aurora") : "aurora";
        string normalized = string.Equals(raw.Trim(), "frost", StringComparison.OrdinalIgnoreCase) ? "frost" : "aurora";
        _settings.UiTheme = normalized;
        if (payload.TryGetProperty("layout", out var lp) && lp.ValueKind == JsonValueKind.String)
        {
            _settings.UiLayout = string.Equals(lp.GetString()?.Trim(), "cockpit", StringComparison.OrdinalIgnoreCase) ? "cockpit" : "bento";
        }
        _settingsStore.Save(_settings);
        BroadcastStateSnapshot();
        return new { ok = true, theme = normalized, layout = _settings.UiLayout };
    }

    private object SetUiLayout(JsonElement payload)
    {
        string raw = payload.TryGetProperty("layout", out var lp) ? (lp.GetString() ?? "bento") : "bento";
        string normalized = string.Equals(raw.Trim(), "cockpit", StringComparison.OrdinalIgnoreCase) ? "cockpit" : "bento";
        _settings.UiLayout = normalized;
        _settingsStore.Save(_settings);
        BroadcastStateSnapshot();
        return new { ok = true, layout = normalized };
    }

    private async Task<object> ToggleOfflineWifiAsync(JsonElement payload)
    {
        bool? targetOff = payload.ValueKind == JsonValueKind.Object &&
                          payload.TryGetProperty("radioOff", out var ro) &&
                          ro.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? ro.GetBoolean()
            : null;

        string msg = await Task.Run(() => _toolbox.ToggleOfflineWifi(targetOff)).ConfigureAwait(false);
        var (supported, isOff) = _toolbox.GetWifiRadioStatus(forceRefresh: true);
        var sys = EnrichSystemState(_systemControl.GetState(targetPid: ResolveTargetRenderPid()));
        _latestSystemState = sys;
        BroadcastStateSnapshot();
        return new
        {
            ok = true,
            wifiSupported = supported,
            wifiRadioOff = isOff,
            message = msg,
            systemControl = sys
        };
    }

    private void UpdateSteadyEcoAutomation(GameEntry? running, bool allowAutoToast, bool forceImmediate = false)
    {
        lock (_steadyEcoLock)
        {
            string mode = (_settings.SteadyEcoMode ?? "off").Trim().ToLowerInvariant();
            bool anySubEnabled = _settings.SteadyEcoCpuEnabled ||
                                 _settings.SteadyEcoBackgroundEnabled ||
                                 _settings.SteadyEcoPeripheralEnabled;

            bool gameRunningNow = running is { IsCurrentlyRunning: true };
            if (gameRunningNow)
            {
                _lastSteadyGameSeenUtc = DateTime.UtcNow;
            }

            bool wantActive = false;
            if (anySubEnabled)
            {
                if (mode == "always")
                {
                    wantActive = true;
                }
                else if (mode == "auto")
                {
                    if (gameRunningNow)
                    {
                        wantActive = true;
                    }
                    else if (!forceImmediate &&
                             _steadyEcoActive &&
                             (DateTime.UtcNow - _lastSteadyGameSeenUtc) < TimeSpan.FromSeconds(3.2))
                    {
                        // 3.2s debounce prevents rapid rollback/re-apply during launcher-to-engine handoffs
                        wantActive = true;
                    }
                }
            }

            if (!wantActive)
            {
                if (_steadyEcoActive || _powerOptimizer.IsApplied || _steadyEcoIsolator.IsolatedCount > 0)
                {
                    bool wasActive = _steadyEcoActive;
                    _steadyEcoIsolator.RestoreAll();
                    _powerOptimizer.Restore();
                    _systemControl.InvalidatePowerSchemeCache();
                    _steadyEcoActive = false;
                    _lastAppliedSteadyPowerOpts = null;
                    _lastSteadyIsolatedGamePid = 0;

                    if (wasActive && allowAutoToast && mode == "auto")
                    {
                        BroadcastEvent("toast", new
                        {
                            kind = "info",
                            message = "已退出游戏 · 稳帧净空已自动还原系统默认策略"
                        });
                    }
                }
                return;
            }

            var topo = CpuTopologyInspector.GetCurrent();
            bool isOnlineAntiCheat = gameRunningNow &&
                                     running?.AntiCheat is { RiskLevel: not AntiCheatRiskLevel.None };

            var powerOpts = new SteadyEcoPowerOptions(
                EnableCpuCoreEco: _settings.SteadyEcoCpuEnabled,
                EnablePeripheralEco: _settings.SteadyEcoPeripheralEnabled,
                CpuEppIndex: Math.Clamp(_settings.SteadyEcoCpuEpp, 50, 90),
                TopologyKind: topo.Kind,
                IsOnlineAntiCheatGame: isOnlineAntiCheat);

            bool newlyActivated = !_steadyEcoActive;
            if (newlyActivated || forceImmediate || !Equals(powerOpts, _lastAppliedSteadyPowerOpts))
            {
                _powerOptimizer.Apply(powerOpts);
                _systemControl.InvalidatePowerSchemeCache();
                _lastAppliedSteadyPowerOpts = powerOpts;
            }

            int activePid = gameRunningNow ? (running?.RunningPid ?? 0) : 0;
            if (_settings.SteadyEcoBackgroundEnabled && !IsVerifyOrCaptureCliRun)
            {
                // Run background isolation once on activation or game PID switch (never rescan Process.GetProcesses() periodically mid-game)
                bool duePass = newlyActivated ||
                               forceImmediate ||
                               (activePid > 0 && activePid != _lastSteadyIsolatedGamePid);
                if (duePass)
                {
                    _steadyEcoIsolator.ApplyPass(
                        topo.BackgroundCpuSetIds,
                        activePid,
                        running?.InstallDirectory,
                        running?.TargetRenderDirectory);
                    _lastBackgroundIsolatePassUtc = DateTime.UtcNow;
                    _lastSteadyIsolatedGamePid = activePid;
                }
            }
            else if (!_settings.SteadyEcoBackgroundEnabled && _steadyEcoIsolator.IsolatedCount > 0)
            {
                _steadyEcoIsolator.RestoreAll();
            }

            _steadyEcoActive = true;

            if (newlyActivated && allowAutoToast && mode == "auto" && gameRunningNow)
            {
                BroadcastEvent("toast", new
                {
                    kind = "info",
                    message = $"已随《{running!.Title}》开启稳帧省电 · CPU EPP 与净空隔离生效"
                });
            }
        }
    }

    private sealed class VerifyMemoryPowerBackend : IPowerSchemeBackend
    {
        private readonly Guid _scheme = new("381b4222-f694-41f0-9685-ff5bb260df2e");
        private readonly Dictionary<(Guid, Guid), (uint Ac, uint Dc)> _values = new();
        private Guid _overlay = new("ded574b5-45a0-4f42-8737-46345c09c238");

        public Guid GetActiveScheme() => _scheme;

        public bool TryReadAcDcValues(Guid scheme, Guid subGroup, Guid setting, out uint acValue, out uint dcValue)
        {
            if (_values.TryGetValue((subGroup, setting), out var pair))
            {
                acValue = pair.Ac;
                dcValue = pair.Dc;
                return true;
            }
            acValue = 50u;
            dcValue = 50u;
            return true;
        }

        public bool TryWriteAcDcValues(Guid scheme, Guid subGroup, Guid setting, uint acValue, uint dcValue)
        {
            _values[(subGroup, setting)] = (acValue, dcValue);
            return true;
        }

        public bool ActivateScheme(Guid scheme) => true;

        public bool TryGetOverlayScheme(out Guid overlayScheme)
        {
            overlayScheme = _overlay;
            return true;
        }

        public bool TrySetOverlayScheme(Guid overlayScheme)
        {
            _overlay = overlayScheme;
            return true;
        }
    }

    private object SetCpuMaxFreq(JsonElement payload)
    {
        int mhz = payload.GetProperty("mhz").GetInt32();
        int normalized = mhz <= 0 ? 0 : Math.Clamp(mhz, 1600, 5500);
        var updated = _systemControl.SetCpuMaxFreqMhz(normalized);
        _settings.CpuMaxFreqMhz = normalized;
        _settingsStore.Save(_settings);
        BroadcastSystemState(updated);
        BroadcastStateSnapshot();
        return updated;
    }

    private async Task<object> SetBrightnessFastAsync(JsonElement payload)
    {
        int percent = Math.Clamp(payload.GetProperty("percent").GetInt32(), 0, 100);
        await _brightFastGate.WaitAsync().ConfigureAwait(false);
        try
        {
            int applied = await Task.Run(() => _systemControl.SetBrightnessFast(percent)).ConfigureAwait(false);
            if (_latestSystemState is { } snap)
            {
                snap.BrightnessPercent = applied;
                _systemControl.RecomputePowerFlow(snap);
                BroadcastEvent("controlValuePatched", new
                {
                    kind = "brightness",
                    brightnessPercent = applied,
                    systemDrawWatts = snap.SystemDrawWatts,
                    estimatedStandbyMinutes = snap.EstimatedStandbyMinutes,
                    estimatedChargerWatts = snap.EstimatedChargerWatts,
                    batteryEtaMinutes = snap.BatteryEtaMinutes
                });
            }
            else
            {
                BroadcastEvent("controlValuePatched", new
                {
                    kind = "brightness",
                    brightnessPercent = applied
                });
            }
            return new { ok = true, brightnessPercent = applied };
        }
        finally
        {
            _brightFastGate.Release();
        }
    }

    private async Task<object> SetVolumeFastAsync(JsonElement payload)
    {
        int percent = Math.Clamp(payload.GetProperty("percent").GetInt32(), 0, 100);
        await _volFastGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var (vol, muted) = await Task.Run(() => _systemControl.SetVolumeFast(percent)).ConfigureAwait(false);
            if (_latestSystemState is { } snap)
            {
                snap.VolumePercent = vol;
                snap.IsMuted = muted;
            }
            BroadcastEvent("controlValuePatched", new
            {
                kind = "volume",
                volumePercent = vol,
                isMuted = muted
            });
            return new { ok = true, volumePercent = vol, isMuted = muted };
        }
        finally
        {
            _volFastGate.Release();
        }
    }

    private async Task<object> SetTdpAsync(JsonElement payload)
    {
        int watts = Math.Clamp(payload.GetProperty("watts").GetInt32(), 8, 120);
        _settings.CurrentTdpWatts = watts;

        // If a running game has an active per-game profile, sync its TDP so it never silently snaps back to an old watt limit
        if (_runningGame is { IsCurrentlyRunning: true } &&
            _settings.PerGameProfiles.TryGetValue(_runningGame.Id, out var runningProf) &&
            runningProf.Enabled)
        {
            runningProf.TdpWatts = watts;
        }

        if (payload.TryGetProperty("unlockThreeStage", out var ulProp))
        {
            _settings.UnlockThreeStagePower = ulProp.GetBoolean();
        }

        if (payload.TryGetProperty("flatPowerLock", out var flatProp) && flatProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _settings.FlatPowerLock = flatProp.GetBoolean();
        }

        if (_settings.UnlockThreeStagePower)
        {
            if (payload.TryGetProperty("slowWatts", out var sp) && sp.ValueKind == JsonValueKind.Number)
            {
                _settings.SlowPptWatts = Math.Clamp(sp.GetInt32(), 8, 130);
            }
            if (payload.TryGetProperty("fastWatts", out var fp) && fp.ValueKind == JsonValueKind.Number)
            {
                _settings.FastPptWatts = Math.Clamp(fp.GetInt32(), 8, 140);
            }
        }
        else
        {
            _settings.SlowPptWatts = watts;
            _settings.FastPptWatts = _settings.FlatPowerLock ? watts : Math.Min(140, watts + 2);
        }

        ScheduleSaveSettings();

        await _tdpFastGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var hw = _latestSystemState?.HardwareService ?? _systemControl.HardwareClient.GetStatus(forceRefresh: false);
            bool hardwareApplied = false;
            int appliedW = watts;
            int appliedSlow = _settings.SlowPptWatts;
            int appliedFast = _settings.FastPptWatts;
            string note = $"已记录目标功耗 {watts}W（未启用底层硬件服务，请先点击上方一键启用）";

            if (hw.ServiceRunning)
            {
                int? slowArg = _settings.UnlockThreeStagePower ? _settings.SlowPptWatts : watts;
                int? fastArg = _settings.UnlockThreeStagePower
                    ? _settings.FastPptWatts
                    : (_settings.FlatPowerLock ? watts : Math.Min(140, watts + 2));
                var (ok, err, nextHw) = await Task.Run(() => _systemControl.HardwareClient.SetTdp(watts, slowArg, fastArg)).ConfigureAwait(false);
                if (!ok)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(err) ? "下发 SMU TDP 失败" : err);
                }
                hardwareApplied = true;
                appliedW = nextHw.AppliedTdpWatts;
                appliedSlow = nextHw.AppliedSlowWatts;
                appliedFast = nextHw.AppliedFastWatts;

                if (_latestSystemState is { } snap)
                {
                    snap.HardwareService.AppliedTdpWatts = nextHw.AppliedTdpWatts;
                    snap.HardwareService.AppliedSlowWatts = nextHw.AppliedSlowWatts;
                    snap.HardwareService.AppliedFastWatts = nextHw.AppliedFastWatts;
                    snap.HardwareService.HwStapmLimitWatts = nextHw.HwStapmLimitWatts ?? nextHw.AppliedTdpWatts;
                    snap.HardwareService.HwSlowLimitWatts = nextHw.HwSlowLimitWatts ?? nextHw.AppliedSlowWatts;
                    snap.HardwareService.HwFastLimitWatts = nextHw.HwFastLimitWatts ?? nextHw.AppliedFastWatts;
                }

                bool v1Clamped = nextHw.DaemonVersion < PawnIoHardwareDaemon.CurrentDaemonVersion && (watts < 12 || watts > 85);
                note = v1Clamped
                    ? $"旧版硬件服务已钳位应用 {nextHw.AppliedTdpWatts}W（点击「升级重启硬件服务」解锁 8–120W 与三段功耗读回）"
                    : (_settings.UnlockThreeStagePower
                        ? $"已下发 SMU 三段功耗：SPL {nextHw.AppliedTdpWatts}W / sPPT {nextHw.AppliedSlowWatts}W / fPPT {nextHw.AppliedFastWatts}W"
                        : $"Strix Halo APU 功耗上限已即时调整为 {nextHw.AppliedTdpWatts}W");
            }

            OverlayHudUpdated?.Invoke();
            BroadcastEvent("powerSettingsPatched", new
            {
                currentTdpWatts = _settings.CurrentTdpWatts,
                unlockThreeStagePower = _settings.UnlockThreeStagePower,
                slowPptWatts = _settings.SlowPptWatts,
                fastPptWatts = _settings.FastPptWatts,
                hardwareApplied,
                appliedTdpWatts = appliedW,
                appliedSlowWatts = appliedSlow,
                appliedFastWatts = appliedFast,
                message = note
            });

            if (hardwareApplied)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(280).ConfigureAwait(false);
                    try { PushSystemTelemetryTick(); } catch { }
                });
            }

            return new
            {
                ok = true,
                hardwareApplied,
                watts = appliedW,
                currentTdpWatts = _settings.CurrentTdpWatts,
                slowWatts = appliedSlow,
                fastWatts = appliedFast,
                message = note
            };
        }
        finally
        {
            _tdpFastGate.Release();
        }
    }

    private object SavePowerConfig(JsonElement payload)
    {
        if (payload.TryGetProperty("customTdpPresets", out var listProp) && listProp.ValueKind == JsonValueKind.Array)
        {
            var nextPresets = new List<int>();
            foreach (var el in listProp.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Number)
                {
                    nextPresets.Add(Math.Clamp(el.GetInt32(), 8, 120));
                }
            }
            if (nextPresets.Count > 0)
            {
                _settings.CustomTdpPresets = nextPresets.Distinct().OrderBy(w => w).ToList();
            }
        }

        if (payload.TryGetProperty("flatPowerLock", out var flProp) && flProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _settings.FlatPowerLock = flProp.GetBoolean();
            if (_settings.FlatPowerLock && !_settings.UnlockThreeStagePower)
            {
                _settings.SlowPptWatts = _settings.CurrentTdpWatts;
                _settings.FastPptWatts = _settings.CurrentTdpWatts;
            }
        }

        if (payload.TryGetProperty("unlockThreeStagePower", out var ulProp))
        {
            _settings.UnlockThreeStagePower = ulProp.GetBoolean();
        }
        if (payload.TryGetProperty("slowPptWatts", out var spProp) && spProp.ValueKind == JsonValueKind.Number)
        {
            _settings.SlowPptWatts = Math.Clamp(spProp.GetInt32(), 8, 130);
        }
        if (payload.TryGetProperty("fastPptWatts", out var fpProp) && fpProp.ValueKind == JsonValueKind.Number)
        {
            _settings.FastPptWatts = Math.Clamp(fpProp.GetInt32(), 8, 140);
        }

        _settingsStore.Save(_settings);

        bool applyNow = payload.TryGetProperty("applyNow", out var apProp) && apProp.GetBoolean();
        if (applyNow)
        {
            var hw = _systemControl.HardwareClient.GetStatus(forceRefresh: true);
            if (hw.ServiceRunning)
            {
                int slowArg = _settings.UnlockThreeStagePower ? _settings.SlowPptWatts : _settings.CurrentTdpWatts;
                int fastArg = _settings.UnlockThreeStagePower
                    ? _settings.FastPptWatts
                    : (_settings.FlatPowerLock ? _settings.CurrentTdpWatts : Math.Min(140, _settings.CurrentTdpWatts + 2));
                var (ok, err, _) = _systemControl.HardwareClient.SetTdp(_settings.CurrentTdpWatts, slowArg, fastArg);
                if (!ok)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(err) ? "下发 SMU 三段功耗失败" : err);
                }
            }
        }

        var nextSys = _systemControl.GetState(includeHardwareRefresh: applyNow);
        BroadcastSystemState(nextSys);
        BroadcastStateSnapshot();
        return new { ok = true, settings = _settings, systemControl = nextSys };
    }

    private object SetPowerLinkage(JsonElement payload)
    {
        bool enabled = payload.TryGetProperty("enabled", out var enProp) ? enProp.GetBoolean() : _settings.PowerLinkageEnabled;
        int batteryW = payload.TryGetProperty("batteryWatts", out var bwProp) ? Math.Clamp(bwProp.GetInt32(), 8, 120) : _settings.BatteryTdpWatts;
        int acW = payload.TryGetProperty("acWatts", out var awProp) ? Math.Clamp(awProp.GetInt32(), 8, 120) : _settings.AcTdpWatts;

        _settings.PowerLinkageEnabled = enabled;
        _settings.BatteryTdpWatts = batteryW;
        _settings.AcTdpWatts = acW;

        var curSys = _systemControl.GetState();
        if (enabled)
        {
            int target = curSys.IsAcOnline ? acW : batteryW;
            _settings.CurrentTdpWatts = target;
            if (curSys.HardwareService.ServiceRunning)
            {
                _systemControl.HardwareClient.SetTdp(target);
            }
        }

        _settingsStore.Save(_settings);
        BroadcastStateSnapshot();
        return new { ok = true, settings = _settings };
    }

    private object SetFanMode(JsonElement payload)
    {
        string rawMode = (payload.TryGetProperty("mode", out var mp) ? mp.GetString() : "auto") ?? "auto";
        int manualPct = payload.TryGetProperty("manualPercent", out var pp)
            ? Math.Clamp(pp.GetInt32(), 20, 100)
            : _settings.ManualFanPercent;

        if (payload.TryGetProperty("curveId", out var cidProp) && cidProp.GetString() is { Length: > 0 } cid)
        {
            _settings.ActiveFanCurveId = cid;
        }

        if (payload.TryGetProperty("curves", out var curvesProp) && curvesProp.ValueKind == JsonValueKind.Array)
        {
            var parsed = JsonSerializer.Deserialize<List<FanCurveProfile>>(curvesProp.GetRawText(), JsonOptions);
            if (parsed is { Count: > 0 })
            {
                _settings.FanCurves = FanCurveEvaluator.NormalizeCurves(parsed);
            }
        }

        string mode = rawMode.Trim().ToLowerInvariant() switch
        {
            "auto" => "auto",
            "manual" => "manual",
            "curve" or "silent" or "balanced" or "turbo" => "curve",
            _ => "auto"
        };

        _settings.FanMode = mode;
        _settings.ManualFanPercent = manualPct;
        var activeCurve = FanCurveEvaluator.ResolveActiveCurve(_settings.FanCurves, _settings.ActiveFanCurveId);
        _settings.ActiveFanCurveId = activeCurve.Id;
        _settingsStore.Save(_settings);

        var curSys = _systemControl.GetState(includeHardwareRefresh: true);
        bool hardwareApplied = false;
        if (curSys.HardwareService.ServiceRunning)
        {
            int tempC = Math.Max(curSys.GpuTempC, curSys.CpuTempC);
            var (ok, err, _) = _systemControl.HardwareClient.SetFan(mode, manualPct, tempC, activeCurve.Points);
            if (!ok)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(err) ? "下发 EC 风扇策略失败" : err);
            }
            hardwareApplied = true;
        }

        var nextSys = _systemControl.GetState(includeHardwareRefresh: true);
        BroadcastSystemState(nextSys);
        BroadcastStateSnapshot();

        bool gpdConflict = hardwareApplied && mode != "auto" && nextSys.HardwareService.GpdServiceRunning;
        string modeText = mode switch
        {
            "auto" => "EC 自动",
            "manual" => $"直接固定 {manualPct}%",
            _ => $"温度曲线 ({activeCurve.Name})"
        };
        string msg = !hardwareApplied
            ? $"已保存风扇策略：{modeText}（启用底层硬件服务后生效）"
            : gpdConflict
                ? $"风扇已设为 {modeText}（警告：检测到 GPDToolService 正在运行会覆盖风扇转速，请在硬件监控页暂停 GPD 服务）"
                : $"风扇模式已切换为 {modeText}";
        return new
        {
            ok = true,
            hardwareApplied,
            gpdConflict,
            message = msg,
            systemControl = nextSys
        };
    }

    private object SaveFanCurves(JsonElement payload)
    {
        if (payload.TryGetProperty("curves", out var curvesProp) && curvesProp.ValueKind == JsonValueKind.Array)
        {
            var parsed = JsonSerializer.Deserialize<List<FanCurveProfile>>(curvesProp.GetRawText(), JsonOptions);
            _settings.FanCurves = FanCurveEvaluator.NormalizeCurves(parsed);
        }

        if (payload.TryGetProperty("activeCurveId", out var cidProp) && cidProp.GetString() is { Length: > 0 } cid)
        {
            _settings.ActiveFanCurveId = cid;
        }

        var activeCurve = FanCurveEvaluator.ResolveActiveCurve(_settings.FanCurves, _settings.ActiveFanCurveId);
        _settings.ActiveFanCurveId = activeCurve.Id;
        bool activate = payload.TryGetProperty("activate", out var actProp) && actProp.GetBoolean();
        if (activate)
        {
            _settings.FanMode = "curve";
        }
        _settingsStore.Save(_settings);

        var sys = _systemControl.GetState(includeHardwareRefresh: true);
        if (_settings.FanMode == "curve" && sys.HardwareService.ServiceRunning)
        {
            int tempC = Math.Max(sys.GpuTempC, sys.CpuTempC);
            var (ok, err, _) = _systemControl.HardwareClient.SetFan("curve", _settings.ManualFanPercent, tempC, activeCurve.Points);
            if (!ok)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(err) ? "更新 EC 风扇曲线失败" : err);
            }
            sys = _systemControl.GetState(includeHardwareRefresh: true);
            BroadcastSystemState(sys);
        }

        BroadcastStateSnapshot();
        return new { ok = true, settings = _settings, activeCurve, systemControl = sys };
    }

    private object OpenSettingsJson()
    {
        FlushPendingSettingsSave();
        _settingsStore.Save(_settings);
        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{_settingsStore.SettingsFilePath}\"",
            UseShellExecute = true
        });
        return new { ok = true, path = _settingsStore.SettingsFilePath, message = "已打开 settings.json，修改保存后点击「重载」即可立即生效" };
    }

    private object ReloadSettingsJson()
    {
        _settings = _settingsStore.Load();
        _settings.UiTheme = string.Equals(_settings.UiTheme, "frost", StringComparison.OrdinalIgnoreCase) ? "frost" : "aurora";
        _settings.UiLayout = string.Equals(_settings.UiLayout, "cockpit", StringComparison.OrdinalIgnoreCase) ? "cockpit" : "bento";
        var sys = _systemControl.GetState(includeHardwareRefresh: true);
        if (sys.HardwareService.ServiceRunning)
        {
            int slowArg = _settings.UnlockThreeStagePower ? _settings.SlowPptWatts : _settings.CurrentTdpWatts;
            int fastArg = _settings.UnlockThreeStagePower ? _settings.FastPptWatts : Math.Min(140, _settings.CurrentTdpWatts + 2);
            try { _systemControl.HardwareClient.SetTdp(_settings.CurrentTdpWatts, slowArg, fastArg); } catch { }

            var activeCurve = FanCurveEvaluator.ResolveActiveCurve(_settings.FanCurves, _settings.ActiveFanCurveId);
            try { _systemControl.HardwareClient.SetFan(_settings.FanMode, _settings.ManualFanPercent, Math.Max(sys.GpuTempC, sys.CpuTempC), activeCurve.Points); } catch { }
            sys = _systemControl.GetState(includeHardwareRefresh: true);
        }

        ApplyWindowsStartup(_settings.LaunchAtWindowsStartup, _settings.RunAsAdministrator);
        _inputHub.GamepadMouseModeEnabled = _settings.GamepadMouseModeEnabled;
        _inputHub.UpdateSettings(_settings);
        OverlayHudUpdated?.Invoke();
        BroadcastSystemState(sys);
        BroadcastStateSnapshot();
        return new { ok = true, settings = _settings, systemControl = sys, message = "已重新载入 settings.json 并同步电源与风扇配置" };
    }

    public void TriggerPhysicalOskToggle(bool forceClose = false)
    {
        try
        {
            string msg = HandheldToolboxService.ToggleOnScreenKeyboard(
                _settings.PreferredOskMode,
                fromUiClick: false,
                forceClose: forceClose,
                fromPhysicalChord: true);
            BroadcastEvent("toast", new { kind = "info", message = msg });
        }
        catch (Exception ex)
        {
            BroadcastEvent("toast", new { kind = "warn", message = $"屏幕键盘切换失败: {ex.Message}" });
        }
    }

    private async Task<object> SetOverlayHudAsync(JsonElement payload)
    {
        if (payload.TryGetProperty("level", out var lvlProp))
        {
            _settings.OverlayLevel = Math.Clamp(lvlProp.GetInt32(), 0, 4);
        }
        if (payload.TryGetProperty("position", out var posProp) && posProp.GetString() is { Length: > 0 } pos)
        {
            _settings.OverlayPosition = pos is "top-left" or "top-center" or "top-right" ? pos : "top-center";
        }
        if (payload.TryGetProperty("opacity", out var opProp))
        {
            _settings.OverlayOpacity = Math.Clamp(opProp.GetDouble(), 0.35, 1.0);
        }

        bool rendererRequested = false;
        if (payload.TryGetProperty("renderer", out var rndProp) && rndProp.GetString() is { Length: > 0 } rnd)
        {
            _settings.OverlayRenderer = RtssOsdFormatter.NormalizeRendererMode(rnd);
            rendererRequested = true;
        }

        _settingsStore.Save(_settings);

        RtssEnsureResult? rtssResult = null;
        bool autoPrepare = rendererRequested &&
                           (!payload.TryGetProperty("autoPrepareRtss", out var prepProp) || prepProp.GetBoolean());
        if (autoPrepare && _settings.OverlayRenderer is RtssOsdFormatter.ModeAutoRtss or RtssOsdFormatter.ModeRtss)
        {
            rtssResult = await _rtssClient.EnsureRtssRunningAsync(allowInstall: true).ConfigureAwait(false);
        }

        OverlayHudUpdated?.Invoke();
        BroadcastStateSnapshot();

        bool running = rtssResult?.Running ?? _rtssClient.IsRtssRunning(forceProbe: rendererRequested);
        bool installed = rtssResult?.Installed ?? _rtssClient.IsRtssInstalled();
        return new
        {
            ok = true,
            settings = _settings,
            rtssRunning = running,
            rtssInstalled = installed,
            launched = rtssResult?.Launched ?? false,
            installedNow = rtssResult?.InstalledNow ?? false,
            message = rtssResult?.Message ?? string.Empty,
            activeOverlayBackend = ActiveOverlayBackend
        };
    }

    private async Task<object> EnsureRtssReadyAsync()
    {
        var res = await _rtssClient.EnsureRtssRunningAsync(allowInstall: true).ConfigureAwait(false);
        OverlayHudUpdated?.Invoke();
        BroadcastStateSnapshot();
        return new
        {
            ok = res.Running,
            rtssRunning = res.Running,
            rtssInstalled = res.Installed,
            launched = res.Launched,
            installedNow = res.InstalledNow,
            message = res.Message,
            activeOverlayBackend = ActiveOverlayBackend
        };
    }

    private object SetGamepadMouseMode(JsonElement payload)
    {
        bool enabled = payload.GetProperty("enabled").GetBoolean();
        _settings.GamepadMouseModeEnabled = enabled;
        _settingsStore.Save(_settings);
        _inputHub.GamepadMouseModeEnabled = enabled;
        BroadcastStateSnapshot();
        return new { ok = true, enabled };
    }

    private async Task<object> InstallHardwareServiceAsync()
    {
        string exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定当前 HaloDeck.exe 路径。");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "--install-hw-service",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("无法拉起管理员安装进程。");
            await proc.WaitForExitAsync().ConfigureAwait(false);
            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException($"注册 HaloDeckHardwareSvc 失败（退出码 {proc.ExitCode}）。请确认已安装 PawnIO 驱动或杀毒软件未拦截服务注册。");
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("已取消管理员授权，将继续以普通用户态模式运行。");
        }

        await Task.Delay(350).ConfigureAwait(false);
        var sys = _systemControl.GetState(includeHardwareRefresh: true);
        if (sys.HardwareService.ServiceRunning)
        {
            int slowArg = _settings.UnlockThreeStagePower ? _settings.SlowPptWatts : _settings.CurrentTdpWatts;
            int fastArg = _settings.UnlockThreeStagePower ? _settings.FastPptWatts : Math.Min(140, _settings.CurrentTdpWatts + 2);
            _systemControl.HardwareClient.SetTdp(Math.Clamp(_settings.CurrentTdpWatts, 8, 120), slowArg, fastArg);
            var activeCurve = FanCurveEvaluator.ResolveActiveCurve(_settings.FanCurves, _settings.ActiveFanCurveId);
            _systemControl.HardwareClient.SetFan(_settings.FanMode, _settings.ManualFanPercent, Math.Max(sys.GpuTempC, sys.CpuTempC), activeCurve.Points);
            sys = _systemControl.GetState(includeHardwareRefresh: true);
        }

        BroadcastSystemState(sys);
        BroadcastStateSnapshot();
        return new
        {
            ok = sys.HardwareService.SmuReady,
            smuReady = sys.HardwareService.SmuReady,
            systemControl = sys,
            message = sys.HardwareService.SmuReady
                ? "已成功启用 HaloDeck 底层硬件服务（后续免 UAC 直控 TDP 与风扇）"
                : $"服务已启动但底层驱动未就绪：{sys.HardwareService.LastError}"
        };
    }

    private async Task<object> ToggleGpdServiceAsync(JsonElement payload)
    {
        bool paused = payload.GetProperty("paused").GetBoolean();
        var hw = _systemControl.HardwareClient.GetStatus(forceRefresh: true);

        if (hw.ServiceRunning)
        {
            var (ok, err, nextHw) = _systemControl.HardwareClient.SetGpdServicePaused(paused);
            if (!ok) throw new InvalidOperationException(string.IsNullOrWhiteSpace(err) ? "切换 GPDToolService 失败" : err);
            var sys = _systemControl.GetState(includeHardwareRefresh: true);
            BroadcastSystemState(sys);
            BroadcastStateSnapshot();
            return new { ok = true, paused, hardwareService = nextHw, message = paused ? "已暂停 GPDToolService（避免冲突）" : "已恢复运行 GPDToolService" };
        }

        string exePath = Environment.ProcessPath ?? throw new InvalidOperationException("找不到主程序路径");
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = paused ? "--pause-gpd-service" : "--resume-gpd-service",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        try
        {
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法请求管理员权限。");
            await proc.WaitForExitAsync().ConfigureAwait(false);
            if (proc.ExitCode != 0) throw new InvalidOperationException($"切换 GPDToolService 失败 (退出码 {proc.ExitCode})");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("用户已取消管理员授权。");
        }

        var nextSys = _systemControl.GetState(includeHardwareRefresh: true);
        BroadcastSystemState(nextSys);
        BroadcastStateSnapshot();
        return new { ok = true, paused, systemControl = nextSys, message = paused ? "已暂停 GPDToolService" : "已恢复运行 GPDToolService" };
    }

    private async Task<object> RunToolboxActionAsync(JsonElement payload)
    {
        string action = payload.GetProperty("action").GetString() ?? "";
        int pid = payload.TryGetProperty("pid", out var pidProp) ? pidProp.GetInt32() : 0;
        bool fromQam = payload.TryGetProperty("fromQam", out var qamProp) && qamProp.GetBoolean();

        if (pid <= 0 && _runningGame is { IsCurrentlyRunning: true })
        {
            pid = _runningGame.RunningPid ?? 0;
        }

        string? gameName = _runningGame?.Title;
        bool hasAntiCheat = _runningGame?.AntiCheat?.RiskLevel == AntiCheatRiskLevel.HighRisk;

        if (action == "toggle_fridge" && pid <= 0 && _frozenFallbackPid() is int frozenPid)
        {
            pid = frozenPid;
            gameName = _toolbox.GetFrozenGames().FirstOrDefault(f => f.Pid == frozenPid)?.GameName;
            hasAntiCheat = false;
        }

        bool? forceCloseOsk = action == "osk" ? HandheldToolboxService.ShouldCloseOnUiClick() : null;

        if (fromQam && ((action == "osk" && forceCloseOsk != true) || action is "show_desktop" or "task_view" or "screenshot"))
        {
            InvokeUiAction(() => RequestHideQam?.Invoke());
            await Task.Delay(95).ConfigureAwait(false);
        }

        string msg = await Task.Run(() => _toolbox.ExecuteAction(
            action,
            pid,
            gameName,
            hasAntiCheat,
            _settings.PreferredOskMode,
            fromUiClick: action == "osk",
            forceCloseOsk: forceCloseOsk)).ConfigureAwait(false);
        BroadcastStateSnapshot();
        return new { ok = true, message = msg, frozenGames = _toolbox.GetFrozenGames() };

        int? _frozenFallbackPid() => _toolbox.GetFrozenGames().FirstOrDefault()?.Pid;
    }

    private static List<ushort>? ParseComboCodes(JsonElement payload, string propName)
    {
        if (!payload.TryGetProperty(propName, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<ushort>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int v) && v > 0 && v <= ushort.MaxValue)
            {
                list.Add((ushort)v);
            }
        }
        return list.Count > 0 ? list : null;
    }

    private static List<ushort> CollectAssignedBackButtonFnCodes(GpdControllerConfig? cfg)
    {
        var codes = new HashSet<ushort>();
        if (cfg is not { Connected: true }) return [];

        static void AddIfFn(HashSet<ushort> dst, ushort code)
        {
            if (code is >= 0x0068 and <= 0x006B) dst.Add(code);
        }

        if (cfg.L4ComboCodes is { Count: >= 2 })
        {
            foreach (var c in cfg.L4ComboCodes) AddIfFn(codes, c);
        }
        else if (cfg.L4Mode == 0 && cfg.L4Slots > 0)
        {
            AddIfFn(codes, cfg.L4KeyCode);
        }

        if (cfg.R4ComboCodes is { Count: >= 2 })
        {
            foreach (var c in cfg.R4ComboCodes) AddIfFn(codes, c);
        }
        else if (cfg.R4Mode == 0 && cfg.R4Slots > 0)
        {
            AddIfFn(codes, cfg.R4KeyCode);
        }

        return codes.ToList();
    }

    private async Task<GpdControllerConfig> ReadGpdControllerAsync(JsonElement payload)
    {
        bool force = payload.TryGetProperty("forceRefresh", out var fr) && fr.GetBoolean();
        var cfg = await Task.Run(() => _gpdController.ReadConfig(force)).ConfigureAwait(false);
        if (cfg.Connected)
        {
            var assigned = CollectAssignedBackButtonFnCodes(cfg);
            _inputHub.UpdateAssignedBackButtonFnCodes(assigned);
        }
        return cfg;
    }

    private async Task<GpdControllerConfig> SaveGpdControllerAsync(JsonElement payload)
    {
        var req = new GpdControllerUpdateRequest
        {
            L4SingleHidCode = payload.TryGetProperty("l4Key", out var l4p) && l4p.ValueKind == JsonValueKind.Number ? l4p.GetInt32() : null,
            R4SingleHidCode = payload.TryGetProperty("r4Key", out var r4p) && r4p.ValueKind == JsonValueKind.Number ? r4p.GetInt32() : null,
            L4ComboCodes = ParseComboCodes(payload, "l4Combo"),
            R4ComboCodes = ParseComboCodes(payload, "r4Combo"),
            RumbleLevel = payload.TryGetProperty("rumble", out var rp) ? Math.Clamp(rp.GetInt32(), 0, 2) : 1,
            LeftDeadzone = payload.TryGetProperty("leftDeadzone", out var ldz) ? Math.Clamp(ldz.GetInt32(), -10, 10) : 0,
            RightDeadzone = payload.TryGetProperty("rightDeadzone", out var rdz) ? Math.Clamp(rdz.GetInt32(), -10, 10) : 0
        };

        var updated = await Task.Run(() => _gpdController.WriteConfig(req)).ConfigureAwait(false);

        // Bidirectional sync: only keep F13..F16 takeover enabled for the exact F-keys flashed to L4/R4.
        // Setting back buttons to "关闭背键" (0x0000) or normal gamepad/keyboard keys immediately releases F13..F16!
        var assignedFn = CollectAssignedBackButtonFnCodes(updated);
        _settings.EnableBackButtonHotkeys = assignedFn.Count > 0;
        _settings.BackButtonHotkeysSchemaVersion = 1;
        _settingsStore.Save(_settings);
        _inputHub.UpdateAssignedBackButtonFnCodes(assignedFn);
        _inputHub.UpdateSettings(_settings);

        BroadcastStateSnapshot();
        return updated;
    }

    private object SaveGamePerfProfile(JsonElement payload)
    {
        string gameId = payload.GetProperty("gameId").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(gameId))
        {
            throw new ArgumentException("缺少游戏 ID");
        }

        var profile = new GamePerformanceProfile
        {
            GameId = gameId,
            GameName = payload.TryGetProperty("gameName", out var np) ? (np.GetString() ?? gameId) : gameId,
            Enabled = payload.TryGetProperty("enabled", out var ep) && ep.GetBoolean(),
            TdpWatts = payload.TryGetProperty("tdpWatts", out var tp) ? Math.Clamp(tp.GetInt32(), 8, 120) : 25,
            RefreshRateHz = payload.TryGetProperty("refreshRateHz", out var hp) ? hp.GetInt32() : 0,
            CpuMaxFreqMhz = payload.TryGetProperty("cpuMaxFreqMhz", out var cp) ? (cp.GetInt32() <= 0 ? 0 : Math.Clamp(cp.GetInt32(), 1600, 5500)) : 0
        };

        _settings.PerGameProfiles[gameId] = profile;
        _settingsStore.Save(_settings);

        if (_runningGame is { IsCurrentlyRunning: true } &&
            string.Equals(_runningGame.Id, gameId, StringComparison.OrdinalIgnoreCase) &&
            profile.Enabled)
        {
            var curSys = _systemControl.GetState(includeHardwareRefresh: false);
            if (profile.RefreshRateHz == -1 && curSys.DynamicRefreshSupported && !curSys.IsDynamicRefreshRate)
            {
                try { _systemControl.SetRefreshRate(curSys.DynamicCeilingHz > 0 ? curSys.DynamicCeilingHz : 120, true); } catch { }
            }
            else if (profile.RefreshRateHz > 0 && (curSys.RefreshRateHz != profile.RefreshRateHz || curSys.IsDynamicRefreshRate))
            {
                try { _systemControl.SetRefreshRate(profile.RefreshRateHz, false); } catch { }
            }
            var hw = _systemControl.HardwareClient.GetStatus();
            if (hw.ServiceRunning)
            {
                int fastW = _settings.FlatPowerLock ? profile.TdpWatts : Math.Min(140, profile.TdpWatts + 2);
                _systemControl.HardwareClient.SetTdp(profile.TdpWatts, profile.TdpWatts, fastW);
            }
        }

        BroadcastStateSnapshot();
        return new { ok = true, profile };
    }

    private const string ElevatedStartupTaskName = "HaloDeck_AutoStart";

    private static void ApplyWindowsStartup(bool enabled, bool runAsAdmin = false)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue("HandheldOpti", throwOnMissingValue: false);

            var exePath = Environment.ProcessPath;
            if (enabled && runAsAdmin && !string.IsNullOrWhiteSpace(exePath) && Win32Interop.IsCurrentProcessElevated())
            {
                // HKCU\Run cannot launch elevated apps without UAC prompts; register an ONLOGON HIGHEST task instead.
                if (TryRegisterElevatedStartupTask(exePath))
                {
                    key?.DeleteValue("HaloDeck", throwOnMissingValue: false);
                    return;
                }
            }

            if (Win32Interop.IsCurrentProcessElevated())
            {
                TryDeleteElevatedStartupTask();
            }

            if (key is null) return;
            if (enabled)
            {
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue("HaloDeck", $"\"{exePath}\" --minimized");
                }
            }
            else
            {
                key.DeleteValue("HaloDeck", throwOnMissingValue: false);
            }
        }
        catch
        {
            // Ignore restricted registry / task scheduler errors
        }
    }

    private static bool TryRegisterElevatedStartupTask(string exePath)
    {
        try
        {
            string cmd = $"\\\"{exePath}\\\" --minimized";
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Create /TN \"{ElevatedStartupTaskName}\" /SC ONLOGON /RL HIGHEST /F /TR \"{cmd}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            proc?.WaitForExit(4000);
            return proc is { ExitCode: 0 };
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteElevatedStartupTask()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Delete /TN \"{ElevatedStartupTaskName}\" /F",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            proc?.WaitForExit(3000);
        }
        catch { }
    }

    private void BroadcastStateSnapshot()
    {
        BroadcastEvent("stateChanged", BuildSnapshot());
    }

    private void ReplyToSender(WebView2 sender, string id, bool ok, object? result, string? error)
    {
        var payload = JsonSerializer.Serialize(new { type = "response", id, ok, result, error }, JsonOptions);
        _dispatcher.InvokeAsync(() =>
        {
            try
            {
                sender.CoreWebView2?.PostWebMessageAsJson(payload);
            }
            catch
            {
                // Ignore disposed WebView
            }
        });
    }

    private void BroadcastEvent(string eventName, object data)
    {
        var payload = JsonSerializer.Serialize(new { type = "event", eventName, data }, JsonOptions);
        List<WebView2> views;
        lock (_registeredViews)
        {
            views = _registeredViews.ToList();
        }

        _dispatcher.InvokeAsync(() =>
        {
            foreach (var v in views)
            {
                try
                {
                    v.CoreWebView2?.PostWebMessageAsJson(payload);
                }
                catch
                {
                    // Ignore unready/disposed view
                }
            }
        });
    }
}
