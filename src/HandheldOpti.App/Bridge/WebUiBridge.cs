using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using HandheldOpti.App.Native;
using HandheldOpti.Core.Components;
using HandheldOpti.Core.Config;
using HandheldOpti.Core.Discovery;
using HandheldOpti.Core.Injection;
using HandheldOpti.Core.Models;
using HandheldOpti.Core.Process;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace HandheldOpti.App.Bridge;

public sealed class WebUiBridge
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

    private readonly List<WebView2> _registeredViews = new();
    private Task<CoreWebView2Environment>? _sharedEnvTask;
    private AppSettings _settings;
    private IReadOnlyList<GameEntry> _cachedGames = Array.Empty<GameEntry>();
    private GameEntry? _runningGame;

    public event Action? RequestHideQam;
    public event Action? RequestShowConsole;
    public event Action? RequestToggleFullscreen;
    public event Action? RequestMinimizeToTray;

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

        _inputHub.GamepadCountChanged += count =>
            BroadcastEvent("gamepadStatus", new { connectedCount = count });

        _inputHub.UiGamepadAction += action =>
            BroadcastEvent("gamepadAction", new { action });

        _inputHub.GamepadComboRecorded += buttons =>
            BroadcastEvent("gamepadComboRecorded", new { buttons });
    }

    public AppSettings Settings => _settings;
    public IReadOnlyList<GameEntry> CachedGames => _cachedGames;
    public GameEntry? RunningGame => _runningGame;

    public async Task InitializeAsync()
    {
        _cachedGames = await _libraryService.ScanAllAsync(_settings.ExcludedGameIds).ConfigureAwait(false);
        _runningGame = _runningTracker.Poll(_cachedGames);
        _ = RefreshComponentsCatalogSilentAsync();
    }

    public async Task AttachWebViewAsync(WebView2 webView, string mode)
    {
        if (webView.CoreWebView2 is null)
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HandheldOpti",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);

            _sharedEnvTask ??= CoreWebView2Environment.CreateAsync(null, userDataFolder);
            var env = await _sharedEnvTask;
            await webView.EnsureCoreWebView2Async(env);
        }

        var core = webView.CoreWebView2!;
        var webUiDir = Path.Combine(AppContext.BaseDirectory, "WebUI");
        core.SetVirtualHostNameToFolderMapping(
            "app.handheldopti.local",
            webUiDir,
            CoreWebView2HostResourceAccessKind.Allow);

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

        core.Navigate($"https://app.handheldopti.local/index.html?mode={mode}");
    }

    public void PollRunningAndAutoInjectTick(bool runAutoInjectPass)
    {
        var updatedRunning = _runningTracker.Poll(_cachedGames);
        bool changed = !Equals(updatedRunning?.Id, _runningGame?.Id) ||
                       updatedRunning?.IsCurrentlyRunning != _runningGame?.IsCurrentlyRunning ||
                       updatedRunning?.Injection.IsInjected != _runningGame?.Injection.IsInjected;
        _runningGame = updatedRunning;

        if (changed)
        {
            BroadcastStateSnapshot();
        }

        if (runAutoInjectPass && _settings.BackgroundAutoInjectEnabled)
        {
            _ = RunBackgroundAutoInjectAsync();
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

        await Win32Interop.SendKeyHoldAsync(vk, _settings.InsertHoldDurationMs).ConfigureAwait(false);
        BroadcastEvent("toast", new { kind = "info", message = $"已向当前前台发送呼出键 (0x{vk:X2})" });
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
                "sendInsertKey" => await TriggerInsertFromUiAsync(),
                "hideQam" => InvokeUiAction(() => RequestHideQam?.Invoke()),
                "showConsole" => InvokeUiAction(() => RequestShowConsole?.Invoke()),
                "toggleFullscreen" => InvokeUiAction(() => RequestToggleFullscreen?.Invoke()),
                "minimizeToTray" => InvokeUiAction(() => RequestMinimizeToTray?.Invoke()),
                "runAutoInjectNow" => await RunBackgroundAutoInjectAsync(),
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
        var activeGame = _runningGame ?? _cachedGames.FirstOrDefault();
        return new
        {
            runningGame = _runningGame,
            activeInspectGame = activeGame,
            games = _cachedGames,
            presets = PresetCatalog.GetPresets(),
            components = _componentManager.GetComponents(),
            customExtensions = _componentManager.GetCustomExtensions(_settings.DisabledCustomDlls),
            settings = _settings,
            proxyOptions = InjectionEngine.SupportedProxyNames,
            connectedControllers = _inputHub.ConnectedControllerCount,
            hardwareProfile = new
            {
                deviceName = "GPD Win 5 / Windows 掌机",
                recommendedArch = "AMD Strix Halo · RDNA 3.5",
                menuKeyHint = "L4/R4 推荐绑定 Ctrl+Alt+O / Ctrl+Alt+I"
            }
        };
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
            _runningTracker.SelectManualFallbackGame(game);
            _runningGame = _runningTracker.LastActiveGame;
            BroadcastStateSnapshot();
        }
        return _runningGame;
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

        bool startupChanged = updated.LaunchAtWindowsStartup != _settings.LaunchAtWindowsStartup;
        _settings = updated;
        _settingsStore.Save(_settings);
        _inputHub.UpdateSettings(_settings);

        if (startupChanged)
        {
            ApplyWindowsStartup(_settings.LaunchAtWindowsStartup);
        }

        BroadcastStateSnapshot();
        return _settings;
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

    private static void ApplyWindowsStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return;
            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue("HandheldOpti", $"\"{exePath}\" --minimized");
                }
            }
            else
            {
                key.DeleteValue("HandheldOpti", throwOnMissingValue: false);
            }
        }
        catch
        {
            // Ignore restricted registry errors
        }
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
