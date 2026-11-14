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
    private HwndSource? _hiddenHotkeyHost;
    private int _pollCounter;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool verifyStartup = e.Args.Any(a => string.Equals(a, "--verify-startup", StringComparison.OrdinalIgnoreCase));
        bool verifyComponents = e.Args.Any(a => string.Equals(a, "--verify-components", StringComparison.OrdinalIgnoreCase));
        int verifyGuiIdx = Array.FindIndex(e.Args, a => string.Equals(a, "--verify-gui", StringComparison.OrdinalIgnoreCase));
        bool verifyGui = verifyGuiIdx >= 0;
        string verifyGuiOutDir = (verifyGui && verifyGuiIdx + 1 < e.Args.Length && !e.Args[verifyGuiIdx + 1].StartsWith("--", StringComparison.Ordinal))
            ? e.Args[verifyGuiIdx + 1]
            : Path.Combine(Path.GetTempPath(), "HandheldOptiGuiVerify");
        bool startMinimized = e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        bool startQam = e.Args.Any(a => string.Equals(a, "--qam", StringComparison.OrdinalIgnoreCase));

        if (!verifyStartup && !verifyComponents && !verifyGui)
        {
            _singleInstanceMutex = new Mutex(true, "HandheldOpti.SingleInstance.Mutex", out bool isNew);
            if (!isNew)
            {
                System.Windows.MessageBox.Show(
                    "HandheldOpti 已在后台运行，可通过右下角托盘图标或组合键 (Select + RB / Ctrl+Alt+O) 呼出。",
                    "HandheldOpti",
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

        // Hidden HWND host ensures global RegisterHotKey works even while both windows are hidden in tray
        var hostParams = new HwndSourceParameters("HandheldOptiHotkeyHost")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _hiddenHotkeyHost = new HwndSource(hostParams);
        _inputHub.AttachHotkeyWindow(_hiddenHotkeyHost.Handle);

        if (verifyStartup)
        {
            await RunStartupSelfVerifyAsync(_mainWindow, _bridge, _inputHub);
            Shutdown(0);
            return;
        }

        SetupTrayIcon();
        WireNavigationEvents();

        if (verifyGui)
        {
            int exitCode = await RunGuiSelfVerifyAsync(verifyGuiOutDir, _mainWindow, _qamWindow, _bridge);
            ShutdownApplicationWithCode(exitCode);
            return;
        }

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
        bool mainActive = _mainWindow is not null && _mainWindow.IsVisible && _mainWindow.IsActive;
        bool qamActive = _qamWindow is not null && _qamWindow.IsVisible && _qamWindow.IsActive;
        _inputHub.IsUiActive = mainActive || qamActive;
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
        menu.Items.Add("向当前游戏发送 Insert 键", null, (_, _) =>
        {
            if (_bridge is not null) _ = _bridge.SendInsertKeyToGameAsync();
        });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出 HandheldOpti", null, (_, _) => Dispatcher.Invoke(ShutdownApplication));

        Icon appTrayIcon = (!string.IsNullOrWhiteSpace(Environment.ProcessPath) && File.Exists(Environment.ProcessPath))
            ? (Icon.ExtractAssociatedIcon(Environment.ProcessPath) ?? SystemIcons.Application)
            : SystemIcons.Application;

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = appTrayIcon,
            Text = "HandheldOpti · GPD Win 5 自动 OptiScaler",
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

            bool runningReady = await WaitUntilAsync(
                mainCore,
                "document.readyState === 'complete' && document.querySelectorAll('.preset-card').length === 4");
            await Task.Delay(250);

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

            var runningInfoJson = await mainCore.ExecuteScriptAsync("""
                (() => ({
                  presetCount: document.querySelectorAll('.preset-card').length,
                  fgOptions: document.querySelectorAll('#cfgFgType option').length,
                  legendFits: document.getElementById('gamepadLegend').getBoundingClientRect().bottom <= window.innerHeight + 2,
                  hasInspectBtn: Boolean(document.getElementById('btnInspectLog'))
                }))()
                """);
            using var runningDoc = JsonDocument.Parse(runningInfoJson);
            var runningRoot = runningDoc.RootElement;
            bool runningOk = runningReady &&
                             runningRoot.GetProperty("presetCount").GetInt32() == 4 &&
                             runningRoot.GetProperty("fgOptions").GetInt32() == 5 &&
                             runningRoot.GetProperty("legendFits").GetBoolean();
            bool hasInspectBtn = runningRoot.GetProperty("hasInspectBtn").GetBoolean();
            Record("runningTabAndFooterFit", runningOk, new
            {
                presetCount = runningRoot.GetProperty("presetCount").GetInt32(),
                fgOptions = runningRoot.GetProperty("fgOptions").GetInt32(),
                legendFits = runningRoot.GetProperty("legendFits").GetBoolean(),
                hasInspectBtn
            });

            await CapturePngAsync(mainCore, outputDir, "01_console_running.png");

            await mainCore.ExecuteScriptAsync("document.querySelector('[data-preset=\"xess-antishimmer\"]').click()");
            bool presetSwitched = await WaitUntilAsync(mainCore, "document.getElementById('cfgDx12')?.value === 'xess'");
            await mainCore.ExecuteScriptAsync("document.querySelector('[data-preset=\"strix-halo-quality\"]').click()");
            await WaitUntilAsync(mainCore, "document.getElementById('cfgDx12')?.value === 'auto'");
            Record("presetSwitching", presetSwitched);

            if (hasInspectBtn)
            {
                await mainCore.ExecuteScriptAsync("document.getElementById('btnInspectLog').click()");
                bool modalOpened = await WaitUntilAsync(mainCore, "!document.getElementById('modalBackdrop').classList.contains('hidden')");
                await Task.Delay(200);
                await CapturePngAsync(mainCore, outputDir, "03_console_modal_log.png");
                await mainCore.ExecuteScriptAsync("document.getElementById('btnCloseModal')?.click()");
                bool modalClosed = await WaitUntilAsync(mainCore, "document.getElementById('modalBackdrop').classList.contains('hidden')");
                Record("diagnosticModal", modalOpened && modalClosed, new { modalOpened, modalClosed });
            }
            else
            {
                Record("diagnosticModal", true, "skipped (no games discovered)");
            }

            await mainCore.ExecuteScriptAsync("document.querySelector('[data-tab=\"library\"]').click()");
            bool libReady = await WaitUntilAsync(
                mainCore,
                "document.querySelector('.tab-btn[data-tab=\"library\"]')?.classList.contains('active') && (document.querySelectorAll('.game-card').length > 0 || Boolean(document.getElementById('btnScanGames')))");
            await Task.Delay(250);
            var libCountStr = await mainCore.ExecuteScriptAsync("document.querySelectorAll('.game-card').length");
            int renderedGames = int.TryParse(libCountStr, out var parsedGames) ? parsedGames : -1;
            Record("libraryTab", libReady && renderedGames == bridge.CachedGames.Count, new
            {
                renderedGames,
                cachedGames = bridge.CachedGames.Count
            });
            await CapturePngAsync(mainCore, outputDir, "02_console_library.png");

            await mainCore.ExecuteScriptAsync("document.querySelector('[data-tab=\"components\"]').click()");
            bool compReady = await WaitUntilAsync(mainCore, "document.querySelectorAll('[data-install-comp]').length === 4");
            await Task.Delay(200);
            var hasOptiText = await WaitUntilAsync(mainCore, "document.body.innerText.includes('OptiScaler')");
            Record("componentsTab", compReady && hasOptiText);
            await CapturePngAsync(mainCore, outputDir, "04_console_components.png");

            await mainCore.ExecuteScriptAsync("document.querySelector('[data-tab=\"settings\"]').click()");
            bool settingsReady = await WaitUntilAsync(
                mainCore,
                "Boolean(document.getElementById('setAutoInject') && document.getElementById('btnHotkeyQam')) && document.querySelectorAll('.gp-focused').length === 1");
            await Task.Delay(150);
            var focusedBefore = await mainCore.ExecuteScriptAsync("document.querySelector('.gp-focused')?.id || ''");
            await mainCore.ExecuteScriptAsync("window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))");
            await Task.Delay(120);
            var focusedAfter = await mainCore.ExecuteScriptAsync("document.querySelector('.gp-focused')?.id || ''");
            var focusCountStr = await mainCore.ExecuteScriptAsync("document.querySelectorAll('.gp-focused').length");
            bool focusValid = settingsReady && focusCountStr == "1" && !string.Equals(focusedBefore, focusedAfter, StringComparison.Ordinal);
            Record("settingsAndSpatialFocus", focusValid, new { focusedBefore, focusedAfter, focusCount = focusCountStr });
            await CapturePngAsync(mainCore, outputDir, "05_console_settings.png");

            mainWindow.SetFullscreenMode(true);
            await Task.Delay(250);
            bool fsMaximized = mainWindow.WindowState == WindowState.Maximized && mainWindow.WindowStyle == WindowStyle.None;
            mainWindow.SetFullscreenMode(false);
            await Task.Delay(250);
            bool restoredFits = mainWindow.ActualWidth <= workArea.Width + 1 && mainWindow.ActualHeight <= workArea.Height + 1;
            Record("fullscreenToggle", fsMaximized && restoredFits, new { fsMaximized, restoredFits });

            await mainCore.ExecuteScriptAsync("document.querySelector('[data-tab=\"running\"]').click()");
            await WaitUntilAsync(mainCore, "document.querySelectorAll('.preset-card').length === 4");
            await mainCore.ExecuteScriptAsync("document.getElementById('btnOpenQam').click()");

            var qamDeadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < qamDeadline && !(qamWindow.IsVisible && qamWindow.WebViewInitialized))
            {
                await Task.Delay(100);
            }

            bool qamVisibleAndInit = qamWindow.IsVisible && qamWindow.WebViewInitialized;
            bool qamDomReady = false;
            bool qamWidthOk = false;
            int qamErrCount = 0;

            if (qamVisibleAndInit && qamWindow.QamWebView.CoreWebView2 is { } qamCore)
            {
                qamDomReady = await WaitUntilAsync(
                    qamCore,
                    "document.body.classList.contains('mode-qam') && document.querySelectorAll('.preset-card').length === 4 && document.querySelectorAll('.gp-focused').length === 1");
                await Task.Delay(250);
                qamWidthOk = Math.Abs(qamWindow.ActualWidth - 440) < 2;
                await CapturePngAsync(qamCore, outputDir, "06_qam_overlay.png");

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
            Record("qamOverlayAndClose", qamVisibleAndInit && qamDomReady && qamWidthOk && qamClosed, new
            {
                qamVisibleAndInit,
                qamDomReady,
                qamWidthOk,
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
