using System.Diagnostics;
using System.Drawing;
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
        bool startMinimized = e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        bool startQam = e.Args.Any(a => string.Equals(a, "--qam", StringComparison.OrdinalIgnoreCase));

        if (!verifyStartup && !verifyComponents)
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

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "HandheldOpti · GPD Win 5 自动 OptiScaler",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowOrActivateMainConsole);
    }

    private void ShutdownApplication()
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
        Shutdown(0);
    }
}
