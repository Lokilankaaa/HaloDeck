using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using HandheldOpti.App.Bridge;
using HandheldOpti.App.Native;
using HandheldOpti.Core.Components;
using HandheldOpti.Core.Config;
using HandheldOpti.Core.Discovery;
using HandheldOpti.Core.Injection;
using HandheldOpti.Core.Models;
using HandheldOpti.Core.Process;
using Microsoft.Web.WebView2.Core;

namespace HandheldOpti.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private DispatcherTimer? _pollTimer;
    private InputHubService? _inputHub;
    private WebUiBridge? _bridge;
    private MainWindow? _mainWindow;
    private QamWindow? _qamWindow;
    private OverlayHudWindow? _overlayHudWindow;
    private HwndSource? _hiddenHotkeyHost;
    private int _pollCounter;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var cliArgs = e.Args.Length > 0 ? e.Args : Environment.GetCommandLineArgs().Skip(1).ToArray();
        bool verifyStartup = cliArgs.Any(a => string.Equals(a, "--verify-startup", StringComparison.OrdinalIgnoreCase));
        bool verifyComponents = cliArgs.Any(a => string.Equals(a, "--verify-components", StringComparison.OrdinalIgnoreCase));
        int verifyGuiIdx = Array.FindIndex(cliArgs, a => string.Equals(a, "--verify-gui", StringComparison.OrdinalIgnoreCase));
        bool verifyGui = verifyGuiIdx >= 0;
        string verifyGuiOutDir = (verifyGui && verifyGuiIdx + 1 < cliArgs.Length && !cliArgs[verifyGuiIdx + 1].StartsWith("--", StringComparison.Ordinal))
            ? cliArgs[verifyGuiIdx + 1]
            : Path.Combine(Path.GetTempPath(), "HaloDeckGuiVerify");
        bool startMinimized = cliArgs.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        bool startQam = cliArgs.Any(a => string.Equals(a, "--qam", StringComparison.OrdinalIgnoreCase));

        if (!verifyStartup && !verifyComponents && !verifyGui)
        {
            _singleInstanceMutex = new Mutex(true, "HaloDeck.SingleInstance.Mutex", out bool isNew);
            if (!isNew)
            {
                System.Windows.MessageBox.Show(
                    "HaloDeck 已在后台运行，可通过右下角托盘图标或组合键 (Select + RB / Ctrl+Alt+O) 呼出。",
                    "HaloDeck",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown(0);
                return;
            }
        }

        var settingsStore = new SettingsStore();
        var settings = settingsStore.Load();
        var componentManager = new ComponentManager();

        if (verifyComponents)
        {
            await RunComponentVerifyAsync(componentManager);
            Shutdown(0);
            return;
        }

        var injectionEngine = new InjectionEngine(componentManager);
        var libraryService = new GameLibraryService(injectionEngine);
        var runningTracker = new RunningGameTracker(
            Win32Interop.GetForegroundProcessSnapshot,
            libraryService,
            injectionEngine);
        var backgroundAutoInjector = new BackgroundAutoInjector(injectionEngine);

        _inputHub = new InputHubService(settings);
        _bridge = new WebUiBridge(
            settingsStore,
            componentManager,
            injectionEngine,
            libraryService,
            runningTracker,
            backgroundAutoInjector,
            _inputHub,
            Dispatcher);

        await _bridge.InitializeAsync();

        _mainWindow = new MainWindow(_bridge);
        MainWindow = _mainWindow;
        _qamWindow = new QamWindow(_bridge);
        _overlayHudWindow = new OverlayHudWindow();

        // Hidden HWND host ensures global RegisterHotKey works even while both windows are hidden in tray
        var hostParams = new HwndSourceParameters("HaloDeckHotkeyHost")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _hiddenHotkeyHost = new HwndSource(hostParams);
        _inputHub.AttachHotkeyWindow(_hiddenHotkeyHost.Handle);

        if (verifyStartup)
        {
            _bridge.SuppressHardwareCleanupOnDispose = true;
            await RunStartupSelfVerifyAsync(_mainWindow, _bridge, _inputHub);
            Shutdown(0);
            return;
        }

        SetupTrayIcon();
        WireNavigationEvents();

        if (verifyGui)
        {
            _bridge.SuppressHardwareCleanupOnDispose = true;
            int exitCode = await RunGuiSelfVerifyAsync(verifyGuiOutDir, _mainWindow, _qamWindow, _overlayHudWindow, _bridge);
            ShutdownApplicationWithCode(exitCode);
            return;
        }

        ApplyOverlayHudFromSettings();
        _inputHub.StartPolling();

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _pollTimer.Tick += (_, _) =>
        {
            _pollCounter++;
            bool runAutoInject = _pollCounter % 15 == 0;
            _bridge.PollRunningAndAutoInjectTick(runAutoInject);

            var latestSys = _bridge.LatestSystemState;
            if (_bridge.Settings.OverlayLevel > 0 && _overlayHudWindow is not null)
            {
                _overlayHudWindow.UpdateMetrics(latestSys, _bridge.RunningGame, _bridge.Settings.CurrentTdpWatts);
            }

            if (_pollCounter % 2 == 0 && ((_mainWindow?.IsVisible == true) || (_qamWindow?.IsVisible == true)))
            {
                _bridge.PushSystemTelemetryTick();
            }
            UpdateUiActiveFlag();
        };
        _pollTimer.Start();

        if (settings.LaunchConsoleFullscreen)
        {
            _mainWindow.SetFullscreenMode(true);
        }

        if (startQam)
        {
            await _qamWindow.ShowOverlayAsync();
        }
        else if (!startMinimized && !settings.StartMinimizedToTray)
        {
            _mainWindow.Show();
            _mainWindow.Activate();
        }
        UpdateUiActiveFlag();
    }

    private async Task RunStartupSelfVerifyAsync(MainWindow mainWindow, WebUiBridge bridge, InputHubService inputHub)
    {
        // Trigger handle creation & WebView2 attachment offscreen
        mainWindow.ShowActivated = false;
        mainWindow.Left = -32000;
        mainWindow.Top = -32000;
        mainWindow.Show();
        await mainWindow.EnsureInitializedOffscreenForVerifyAsync();
        await Task.Delay(300);

        var report = new
        {
            webView2Initialized = mainWindow.WebViewInitialized,
            scannedGameCount = bridge.CachedGames.Count,
            connectedControllers = inputHub.ConnectedControllerCount,
            activeOptiScalerSlot = bridge.Settings.ActiveOptiScalerSlot,
            hotkeyHostAttached = _hiddenHotkeyHost?.Handle != 0
        };

        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        mainWindow.AllowRealClose();
        mainWindow.Close();
    }

    private static async Task RunComponentVerifyAsync(ComponentManager componentManager)
    {
        await componentManager.RefreshCatalogAsync();
        var installed = new List<object>();

        foreach (var slot in new[] { ComponentSlot.OptiScalerStable, ComponentSlot.OptiPatcher, ComponentSlot.Fakenvapi })
        {
            var info = await componentManager.InstallOrUpdateAsync(slot);
            installed.Add(new
            {
                slot = info.Slot.ToString(),
                version = info.InstalledVersion,
                directory = info.InstalledDirectory,
                asset = info.LatestAsset?.Name
            });
        }

        Console.WriteLine(JsonSerializer.Serialize(new { installed }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void WireNavigationEvents()
    {
        if (_inputHub is null || _bridge is null || _mainWindow is null || _qamWindow is null)
        {
            return;
        }

        _inputHub.ToggleQamRequested += () =>
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await _qamWindow.ToggleOverlayAsync();
                UpdateUiActiveFlag();
            });
        };

        _inputHub.ToggleConsoleRequested += () =>
        {
            Dispatcher.InvokeAsync(ShowOrActivateMainConsole);
        };

        _inputHub.SendInsertToGameRequested += () =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(120);
                await _bridge.SendInsertKeyToGameAsync();
            });
        };

        _inputHub.CycleOsdRequested += () =>
        {
            Dispatcher.InvokeAsync(() => _bridge.CycleOverlayHud());
        };

        _inputHub.ToggleMouseModeRequested += () =>
        {
            Dispatcher.InvokeAsync(() => _bridge.ToggleGamepadMouseMode());
        };

        _bridge.OverlayHudUpdated += () =>
        {
            Dispatcher.Invoke(ApplyOverlayHudFromSettings);
        };

        _bridge.RequestHideQam += () =>
        {
            _qamWindow.HideAndRestoreGameFocus();
            UpdateUiActiveFlag();
        };

        _bridge.RequestToggleQam += () =>
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await _qamWindow.ToggleOverlayAsync();
                UpdateUiActiveFlag();
            });
        };

        _bridge.RequestShowConsole += ShowOrActivateMainConsole;
        _bridge.RequestToggleFullscreen += () => _mainWindow.ToggleFullscreen();
        _bridge.RequestRepositionQam += () => Dispatcher.Invoke(() => _qamWindow?.RepositionOnRightEdge());
        _bridge.RequestMinimizeToTray += () =>
        {
            _mainWindow.Hide();
            UpdateUiActiveFlag();
        };

        _mainWindow.Activated += (_, _) => UpdateUiActiveFlag();
        _mainWindow.Deactivated += (_, _) => UpdateUiActiveFlag();
        _mainWindow.IsVisibleChanged += (_, _) => UpdateUiActiveFlag();
        _qamWindow.Activated += (_, _) => UpdateUiActiveFlag();
        _qamWindow.Deactivated += (_, _) => UpdateUiActiveFlag();
        _qamWindow.IsVisibleChanged += (_, _) => UpdateUiActiveFlag();
    }

    private void ShowOrActivateMainConsole()
    {
        if (_mainWindow is null) return;
        _qamWindow?.Hide();

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }
        _mainWindow.Activate();
        UpdateUiActiveFlag();
    }

    private void UpdateUiActiveFlag()
    {
        if (_inputHub is null) return;
        if (_qamWindow is not null && _qamWindow.IsVisible)
        {
            _inputHub.ActiveUiTarget = "qam";
            return;
        }

        if (_mainWindow is not null && _mainWindow.IsVisible)
        {
            var mainHwnd = new WindowInteropHelper(_mainWindow).Handle;
            if (_mainWindow.IsActive || Win32Interop.IsWindowOrChildForeground(mainHwnd))
            {
                _inputHub.ActiveUiTarget = "console";
                return;
            }
        }

        _inputHub.ActiveUiTarget = null;
    }

    private void ApplyOverlayHudFromSettings()
    {
        if (_bridge is null || _overlayHudWindow is null) return;
        var s = _bridge.Settings;
        _overlayHudWindow.ApplySettings(s.OverlayLevel, s.OverlayPosition, s.OverlayOpacity);
        if (s.OverlayLevel > 0)
        {
            _overlayHudWindow.UpdateMetrics(_bridge.LatestSystemState, _bridge.RunningGame, s.CurrentTdpWatts);
        }
    }

    private void SetupTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开控制台", null, (_, _) => Dispatcher.Invoke(ShowOrActivateMainConsole));
        menu.Items.Add("切换右侧快捷菜单 (QAM)", null, (_, _) => Dispatcher.InvokeAsync(async () =>
        {
            if (_qamWindow is not null) await _qamWindow.ToggleOverlayAsync();
            UpdateUiActiveFlag();
        }));
        menu.Items.Add("切换性能悬浮窗 (OSD)", null, (_, _) => Dispatcher.Invoke(() => _bridge?.CycleOverlayHud()));
        menu.Items.Add("切换手柄桌面鼠标模式", null, (_, _) => Dispatcher.Invoke(() => _bridge?.ToggleGamepadMouseMode()));
        menu.Items.Add("解冻恢复游戏冰箱进程", null, (_, _) => Dispatcher.Invoke(() => _bridge?.ResumeAllFrozenGames()));
        menu.Items.Add("向当前游戏发送 Insert 键", null, (_, _) =>
        {
            if (_bridge is not null) _ = _bridge.SendInsertKeyToGameAsync();
        });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出 HaloDeck", null, (_, _) => Dispatcher.Invoke(ShutdownApplication));

        Icon appTrayIcon = (!string.IsNullOrWhiteSpace(Environment.ProcessPath) && File.Exists(Environment.ProcessPath))
            ? (Icon.ExtractAssociatedIcon(Environment.ProcessPath) ?? SystemIcons.Application)
            : SystemIcons.Application;

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = appTrayIcon,
            Text = "HaloDeck · GPD Win 5 掌机全能中控",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowOrActivateMainConsole);
    }

    private void ShutdownApplication() => ShutdownApplicationWithCode(0);

    private void ShutdownApplicationWithCode(int exitCode)
    {
        _pollTimer?.Stop();
        _inputHub?.Dispose();
        _hiddenHotkeyHost?.Dispose();
        _overlayHudWindow?.Close();
        _bridge?.Dispose();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _mainWindow?.AllowRealClose();
        _mainWindow?.Close();
        _qamWindow?.Close();
        _singleInstanceMutex?.Dispose();
        Shutdown(exitCode);
    }

    private static async Task<bool> WaitUntilAsync(CoreWebView2 core, string boolExpr, int timeoutMs = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var res = await core.ExecuteScriptAsync($"Boolean({boolExpr})");
                if (string.Equals(res, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // Transient script error while navigating/rendering
            }
            await Task.Delay(100);
        }
        return false;
    }

    private static async Task CapturePngAsync(CoreWebView2 core, string outputDir, string fileName)
    {
        var path = Path.Combine(outputDir, fileName);
        await using var fs = File.Create(path);
        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
    }

    private static async Task<int> RunGuiSelfVerifyAsync(
        string outputDir,
        MainWindow mainWindow,
        QamWindow qamWindow,
        OverlayHudWindow overlayHudWindow,
        WebUiBridge bridge)
    {
        Directory.CreateDirectory(outputDir);
        var checks = new Dictionary<string, object>();
        bool allPassed = true;

        void Record(string name, bool passed, object? detail = null)
        {
            if (!passed) allPassed = false;
            checks[name] = new { passed, detail };
        }

        try
        {
            mainWindow.Show();
            mainWindow.Activate();
            await mainWindow.EnsureInitializedOffscreenForVerifyAsync();

            var mainCore = mainWindow.ConsoleWebView.CoreWebView2
                           ?? throw new InvalidOperationException("Main CoreWebView2 未初始化");

            bool handheldReady = await WaitUntilAsync(
                mainCore,
                "document.readyState === 'complete' && Boolean(document.querySelector('.tab-btn[data-nav=\"handheld\"].active')) && document.querySelectorAll('[data-tdp-watt]').length >= 3 && document.querySelectorAll('[data-tdp-preset]').length === 0 && document.querySelectorAll('[data-fan-mode]').length === 3 && document.querySelectorAll('svg.fan-curve-svg').length === 1 && Boolean(document.getElementById('fanManualRange'))");
            await Task.Delay(350);

            var workArea = SystemParameters.WorkArea;
            bool fitsWorkArea = mainWindow.ActualWidth <= workArea.Width + 1 &&
                                mainWindow.ActualHeight <= workArea.Height + 1;
            Record("windowFitsWorkArea", fitsWorkArea, new
            {
                windowWidth = Math.Round(mainWindow.ActualWidth, 1),
                windowHeight = Math.Round(mainWindow.ActualHeight, 1),
                workAreaWidth = Math.Round(workArea.Width, 1),
                workAreaHeight = Math.Round(workArea.Height, 1)
            });

            var homeInfoJson = await mainCore.ExecuteScriptAsync("""
                (() => ({
                  tdpChips: document.querySelectorAll('[data-tdp-watt]').length,
                  legacyTdpPresets: document.querySelectorAll('[data-tdp-preset]').length,
                  fanModes: document.querySelectorAll('[data-fan-mode]').length,
                  fanCurveSvg: document.querySelectorAll('svg.fan-curve-svg').length,
                  hasFanSlider: Boolean(document.getElementById('fanManualRange')),
                  toolboxTiles: document.querySelectorAll('.toolbox-tile').length,
                  selectCount: document.querySelectorAll('select').length,
                  legendFits: document.getElementById('gamepadLegend').getBoundingClientRect().bottom <= window.innerHeight + 2,
                  noHorizOverflow: document.documentElement.scrollWidth <= document.documentElement.clientWidth + 2,
                  focusCount: document.querySelectorAll('.gp-focused').length
                }))()
                """);
            using var homeDoc = JsonDocument.Parse(homeInfoJson);
            var homeRoot = homeDoc.RootElement;
            bool homeOk = handheldReady &&
                          homeRoot.GetProperty("tdpChips").GetInt32() >= 3 &&
                          homeRoot.GetProperty("legacyTdpPresets").GetInt32() == 0 &&
                          homeRoot.GetProperty("fanModes").GetInt32() == 3 &&
                          homeRoot.GetProperty("fanCurveSvg").GetInt32() == 1 &&
                          homeRoot.GetProperty("hasFanSlider").GetBoolean() &&
                          homeRoot.GetProperty("toolboxTiles").GetInt32() >= 8 &&
                          homeRoot.GetProperty("selectCount").GetInt32() == 0 &&
                          homeRoot.GetProperty("legendFits").GetBoolean() &&
                          homeRoot.GetProperty("noHorizOverflow").GetBoolean();
            Record("handheldHomeDefault", homeOk, homeRoot.Clone());
            await CapturePngAsync(mainCore, outputDir, "v3_01_console_handheld.png");

            // Tab 2: Monitor & Click-Through Overlay HUD Verification
            await mainCore.ExecuteScriptAsync("document.querySelector('.tab-btn[data-nav=\"monitor\"]').click()");
            bool monitorDomOk = await WaitUntilAsync(
                mainCore,
                "document.querySelector('.tab-btn[data-nav=\"monitor\"]')?.classList.contains('active') && document.querySelectorAll('[data-osd-level]').length === 5 && document.querySelectorAll('svg.sparkline-svg').length === 2");
            await Task.Delay(250);

            int prevLevel = bridge.Settings.OverlayLevel;
            string prevPos = bridge.Settings.OverlayPosition;
            double prevOpacity = bridge.Settings.OverlayOpacity;
            overlayHudWindow.ApplySettings(3, prevPos, prevOpacity);
            overlayHudWindow.UpdateMetrics(bridge.LatestSystemState, bridge.RunningGame, bridge.Settings.CurrentTdpWatts);
            await Task.Delay(120);
            var hudHwnd = new WindowInteropHelper(overlayHudWindow).Handle;
            int hudExStyle = hudHwnd != 0 ? Win32Interop.GetWindowLong(hudHwnd, Win32Interop.GWL_EXSTYLE) : 0;
            int requiredHudStyles = Win32Interop.WS_EX_TRANSPARENT | Win32Interop.WS_EX_NOACTIVATE;
            bool hudClickThroughOk = overlayHudWindow.IsVisible && (hudExStyle & requiredHudStyles) == requiredHudStyles;
            overlayHudWindow.ApplySettings(prevLevel, prevPos, prevOpacity);

            Record("monitorAndOverlayClickThrough", monitorDomOk && hudClickThroughOk, new
            {
                monitorDomOk,
                hudClickThroughOk,
                hudExStyle = $"0x{hudExStyle:X}"
            });
            await CapturePngAsync(mainCore, outputDir, "v3_02_console_monitor.png");

            // Tab 3: Controls, GPD V2 & Spatial Focus
            await mainCore.ExecuteScriptAsync("document.querySelector('.tab-btn[data-nav=\"controls\"]').click()");
            bool controlsReady = await WaitUntilAsync(
                mainCore,
                "document.querySelector('.tab-btn[data-nav=\"controls\"]')?.classList.contains('active') && Boolean(document.getElementById('btnReadGpd') && document.getElementById('setAutoInject') && document.getElementById('btnHotkeyQam')) && document.querySelectorAll('.keycap-pill').length >= 4 && document.querySelectorAll('.gp-focused').length === 1");
            await Task.Delay(200);
            await mainCore.ExecuteScriptAsync("window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))");
            await Task.Delay(120);
            var focusCountStr = await mainCore.ExecuteScriptAsync("document.querySelectorAll('.gp-focused').length");
            Record("controlsAndSpatialFocus", controlsReady && focusCountStr == "1", new { controlsReady, focusCount = focusCountStr });
            await CapturePngAsync(mainCore, outputDir, "v3_03_console_controls.png");

            // Tab 4: OptiScaler Suite (Tune, Library, Components)
            await mainCore.ExecuteScriptAsync("document.querySelector('.tab-btn[data-nav=\"opti\"]').click()");
            bool optiTuneReady = await WaitUntilAsync(
                mainCore,
                "document.querySelector('.sub-tab-btn[data-opti-sub=\"tune\"]')?.classList.contains('active') && document.querySelectorAll('.preset-card').length === 4");
            await Task.Delay(250);

            var tuneInfoJson = await mainCore.ExecuteScriptAsync("""
                (() => ({
                  presetCount: document.querySelectorAll('.preset-card').length,
                  srPills: document.querySelectorAll('[data-sr-mode]').length,
                  fgPills: document.querySelectorAll('[data-fg-type]').length,
                  targetChipsCount: document.querySelectorAll('.target-chip').length,
                  hasConsolePickerBtn: Boolean(document.getElementById('btnOpenConsoleGamePicker')),
                  selectCount: document.querySelectorAll('select').length,
                  srActive: document.querySelector('[data-sr-mode].active')?.dataset.srMode || '',
                  fgActive: document.querySelector('[data-fg-type].active')?.dataset.fgType || '',
                  legendFits: document.getElementById('gamepadLegend').getBoundingClientRect().bottom <= window.innerHeight + 2,
                  hasInspectBtn: Boolean(document.getElementById('btnInspectLog')) && !document.getElementById('btnInspectLog').disabled
                }))()
                """);
            using var tuneDoc = JsonDocument.Parse(tuneInfoJson);
            var tuneRoot = tuneDoc.RootElement;
            var activeSnap = bridge.ActiveGameSnapshot;
            var expectedCfg = (activeSnap?.Injection.IsInjected == true && activeSnap.Injection.AppliedConfig is not null)
                ? activeSnap.Injection.AppliedConfig
                : PresetCatalog.GetById("strix-halo-quality").Config;

            bool tuneOk = optiTuneReady &&
                          tuneRoot.GetProperty("presetCount").GetInt32() == 4 &&
                          tuneRoot.GetProperty("srPills").GetInt32() == 4 &&
                          tuneRoot.GetProperty("fgPills").GetInt32() == 3 &&
                          tuneRoot.GetProperty("targetChipsCount").GetInt32() == 0 &&
                          tuneRoot.GetProperty("hasConsolePickerBtn").GetBoolean() &&
                          tuneRoot.GetProperty("selectCount").GetInt32() == 0 &&
                          tuneRoot.GetProperty("srActive").GetString() == expectedCfg.SrMode &&
                          tuneRoot.GetProperty("fgActive").GetString() == expectedCfg.FgType &&
                          tuneRoot.GetProperty("legendFits").GetBoolean();
            bool hasInspectBtn = tuneRoot.GetProperty("hasInspectBtn").GetBoolean();
            Record("optiTuneTab", tuneOk, new
            {
                presetCount = tuneRoot.GetProperty("presetCount").GetInt32(),
                srPills = tuneRoot.GetProperty("srPills").GetInt32(),
                fgPills = tuneRoot.GetProperty("fgPills").GetInt32(),
                srActive = tuneRoot.GetProperty("srActive").GetString(),
                fgActive = tuneRoot.GetProperty("fgActive").GetString(),
                expectedSr = expectedCfg.SrMode,
                expectedFg = expectedCfg.FgType,
                hasInspectBtn
            });
            await CapturePngAsync(mainCore, outputDir, "v3_04_opti_tune.png");

            await mainCore.ExecuteScriptAsync("document.querySelector('[data-preset=\"xess-antishimmer\"]').click()");
            bool switchedToXess = await WaitUntilAsync(mainCore, "document.querySelector('[data-sr-mode].active')?.dataset.srMode === 'xess'");
            await mainCore.ExecuteScriptAsync("document.getElementById('btnToggleAdvanced')?.click()");
            bool advOpened = await WaitUntilAsync(mainCore, "document.querySelectorAll('[data-fg-input]').length === 3");
            await mainCore.ExecuteScriptAsync("document.getElementById('btnToggleAdvanced')?.click()");
            await mainCore.ExecuteScriptAsync("document.querySelector('[data-preset=\"strix-halo-quality\"]').click()");
            await WaitUntilAsync(mainCore, "document.querySelector('[data-sr-mode].active')?.dataset.srMode === 'fsr4_int8'");
            Record("presetAndDisclosureToggle", switchedToXess && advOpened, new { switchedToXess, advOpened });

            if (hasInspectBtn)
            {
                await mainCore.ExecuteScriptAsync("document.getElementById('btnInspectLog').click()");
                bool modalOpened = await WaitUntilAsync(mainCore, "!document.getElementById('modalBackdrop').classList.contains('hidden')");
                await Task.Delay(200);
                await CapturePngAsync(mainCore, outputDir, "v3_04b_opti_ini_modal.png");
                await mainCore.ExecuteScriptAsync("document.getElementById('btnCloseModal')?.click()");
                bool modalClosed = await WaitUntilAsync(mainCore, "document.getElementById('modalBackdrop').classList.contains('hidden')");
                Record("diagnosticModal", modalOpened && modalClosed, new { modalOpened, modalClosed });
            }
            else
            {
                Record("diagnosticModal", true, "skipped (no games discovered)");
            }

            // Opti Sub-tab: Library Posters
            await mainCore.ExecuteScriptAsync("document.querySelector('.sub-tab-btn[data-opti-sub=\"library\"]').click()");
            bool libReady = await WaitUntilAsync(
                mainCore,
                "document.querySelector('.sub-tab-btn[data-opti-sub=\"library\"]')?.classList.contains('active') && document.querySelectorAll('.poster-grid').length === 1");
            await Task.Delay(500);
            var libStatsJson = await mainCore.ExecuteScriptAsync("""
                (() => {
                  const cards = document.querySelectorAll('.poster-card').length;
                  const loadedCovers = Array.from(document.querySelectorAll('.poster-img')).filter(img => img.complete && img.naturalWidth > 0).length;
                  return { cards, loadedCovers };
                })()
                """);
            using var libDoc = JsonDocument.Parse(libStatsJson);
            int renderedGames = libDoc.RootElement.GetProperty("cards").GetInt32();
            int loadedCovers = libDoc.RootElement.GetProperty("loadedCovers").GetInt32();
            bool coversOk = bridge.CachedGames.Count == 0 || loadedCovers > 0;
            Record("optiLibrarySubTab", libReady && renderedGames == bridge.CachedGames.Count && coversOk, new
            {
                renderedGames,
                cachedGames = bridge.CachedGames.Count,
                loadedCovers
            });
            await CapturePngAsync(mainCore, outputDir, "v3_05_opti_library.png");

            // Opti Sub-tab: Components
            await mainCore.ExecuteScriptAsync("document.querySelector('.sub-tab-btn[data-opti-sub=\"components\"]').click()");
            bool compReady = await WaitUntilAsync(
                mainCore,
                "document.querySelector('.sub-tab-btn[data-opti-sub=\"components\"]')?.classList.contains('active') && document.querySelectorAll('[data-install-comp]').length === 4 && document.querySelectorAll('[data-active-slot]').length === 2");
            await Task.Delay(250);
            var activeKernelStr = await mainCore.ExecuteScriptAsync("document.querySelector('[data-active-slot].active')?.dataset.activeSlot || ''");
            Record("optiComponentsSubTab", compReady, new { activeKernelSlot = activeKernelStr.Trim('"') });
            await CapturePngAsync(mainCore, outputDir, "v3_06_opti_components.png");

            // Fullscreen Toggle
            mainWindow.SetFullscreenMode(true);
            await Task.Delay(250);
            bool fsMaximized = mainWindow.WindowState == WindowState.Maximized && mainWindow.WindowStyle == WindowStyle.None;
            mainWindow.SetFullscreenMode(false);
            await Task.Delay(250);
            bool restoredFits = mainWindow.ActualWidth <= workArea.Width + 1 && mainWindow.ActualHeight <= workArea.Height + 1;
            Record("fullscreenToggle", fsMaximized && restoredFits, new { fsMaximized, restoredFits });

            // Return to Handheld Home & open QAM Overlay
            await mainCore.ExecuteScriptAsync("document.querySelector('.tab-btn[data-nav=\"handheld\"]').click()");
            await WaitUntilAsync(mainCore, "document.querySelectorAll('[data-tdp-preset]').length === 3");
            await mainCore.ExecuteScriptAsync("document.getElementById('btnOpenQam').click()");

            var qamDeadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < qamDeadline && !(qamWindow.IsVisible && qamWindow.WebViewInitialized))
            {
                await Task.Delay(100);
            }

            bool qamVisibleAndInit = qamWindow.IsVisible && qamWindow.WebViewInitialized;
            bool qamHandheldReady = false;
            bool qamWidthOk = false;
            bool qamOsdOk = false;
            bool qamControlsOk = false;
            bool qamOptiOk = false;
            int qamErrCount = 0;

            if (qamVisibleAndInit && qamWindow.QamWebView.CoreWebView2 is { } qamCore)
            {
                qamHandheldReady = await WaitUntilAsync(
                    qamCore,
                    "document.body.classList.contains('mode-qam') && !document.getElementById('qamRail').classList.contains('hidden') && document.querySelector('.qam-rail-btn[data-qam-tab=\"handheld\"]')?.classList.contains('active') && document.querySelectorAll('.qam-rail-btn').length === 4 && document.querySelectorAll('select').length === 0 && document.documentElement.scrollWidth <= document.documentElement.clientWidth + 2 && document.querySelectorAll('.gp-focused').length === 1");
                await Task.Delay(250);
                qamWidthOk = Math.Abs(qamWindow.ActualWidth - 440) < 2;
                await CapturePngAsync(qamCore, outputDir, "v3_07_qam_handheld.png");

                await qamCore.ExecuteScriptAsync("document.querySelector('.qam-rail-btn[data-qam-tab=\"osd\"]').click()");
                qamOsdOk = await WaitUntilAsync(
                    qamCore,
                    "document.querySelector('.qam-rail-btn[data-qam-tab=\"osd\"]')?.classList.contains('active') && Boolean(document.querySelector('#qamOsdSparkline svg'))");
                await Task.Delay(200);
                await CapturePngAsync(qamCore, outputDir, "v3_08_qam_osd.png");

                await qamCore.ExecuteScriptAsync("document.querySelector('.qam-rail-btn[data-qam-tab=\"controls\"]').click()");
                qamControlsOk = await WaitUntilAsync(
                    qamCore,
                    "document.querySelector('.qam-rail-btn[data-qam-tab=\"controls\"]')?.classList.contains('active') && Boolean(document.getElementById('swQamMouseMode'))");
                await Task.Delay(150);

                await qamCore.ExecuteScriptAsync("document.querySelector('.qam-rail-btn[data-qam-tab=\"opti\"]').click()");
                qamOptiOk = await WaitUntilAsync(
                    qamCore,
                    "document.querySelector('.qam-rail-btn[data-qam-tab=\"opti\"]')?.classList.contains('active') && Boolean(document.getElementById('qamStatusBanner')) && Boolean(document.getElementById('btnQamOpenGamePicker')) && document.querySelectorAll('.qam-game-switcher').length === 0 && document.querySelectorAll('[data-preset]').length >= 3");
                await Task.Delay(200);
                await CapturePngAsync(qamCore, outputDir, "v3_09_qam_opti.png");

                await qamCore.ExecuteScriptAsync("document.getElementById('btnQamOpenGamePicker').click()");
                bool qamGamesPickerOk = await WaitUntilAsync(
                    qamCore,
                    "Boolean(document.querySelector('.qam-game-picker-view')) && Boolean(document.getElementById('btnQamBackToTune')) && document.querySelectorAll('.qam-game-row').length >= 1");
                await Task.Delay(200);
                await CapturePngAsync(qamCore, outputDir, "v3_10_qam_opti_games.png");
                await qamCore.ExecuteScriptAsync("document.getElementById('btnQamBackToTune').click()");
                await WaitUntilAsync(qamCore, "Boolean(document.getElementById('qamStatusBanner'))");
                qamOptiOk = qamOptiOk && qamGamesPickerOk;

                var qamErrStr = await qamCore.ExecuteScriptAsync("(window.__hoErrors || []).length");
                int.TryParse(qamErrStr, out qamErrCount);

                await qamCore.ExecuteScriptAsync("document.getElementById('btnQamClose').click()");
            }

            var qamHideDeadline = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < qamHideDeadline && qamWindow.IsVisible)
            {
                await Task.Delay(80);
            }
            bool qamClosed = !qamWindow.IsVisible;
            Record("qamHandheldFirstAndClose", qamVisibleAndInit && qamHandheldReady && qamWidthOk && qamOsdOk && qamControlsOk && qamOptiOk && qamClosed, new
            {
                qamVisibleAndInit,
                qamHandheldReady,
                qamWidthOk,
                qamOsdOk,
                qamControlsOk,
                qamOptiOk,
                qamActualWidth = Math.Round(qamWindow.ActualWidth, 1),
                qamClosed
            });

            var mainErrStr = await mainCore.ExecuteScriptAsync("(window.__hoErrors || []).length");
            int.TryParse(mainErrStr, out var mainErrCount);
            var mainErrDump = mainErrCount > 0 ? await mainCore.ExecuteScriptAsync("JSON.stringify(window.__hoErrors)") : "[]";
            Record("zeroJsErrors", mainErrCount == 0 && qamErrCount == 0, new { mainErrCount, qamErrCount, mainErrDump });
        }
        catch (Exception ex)
        {
            allPassed = false;
            checks["fatalException"] = new { passed = false, detail = ex.ToString() };
        }

        var reportJson = JsonSerializer.Serialize(new
        {
            allPassed,
            outputDir,
            checks
        }, new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(Path.Combine(outputDir, "gui_verify_report.json"), reportJson);
        Console.WriteLine(reportJson);
        return allPassed ? 0 : 1;
    }
}
