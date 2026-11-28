using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using HaloDeck.App.Bridge;
using HaloDeck.App.Native;
using HaloDeck.Core.Components;
using HaloDeck.Core.Config;
using HaloDeck.Core.Discovery;
using HaloDeck.Core.Injection;
using HaloDeck.Core.Models;
using HaloDeck.Core.Process;
using HaloDeck.Core.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace HaloDeck.App;

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
    private int _pollInFlight;
    private DispatcherTimer? _topmostGuardTimer;
    private nint _winEventHookForeground;
    private nint _winEventHookMinimizeEnd;
    private Win32Interop.WinEventDelegate? _winEventProc;
    private nint _lastExternalForegroundHwnd;
    private int _consoleCloseVersion;
    private bool _isConsoleClosingShieldActive;
    private bool _mainWasParkedOffscreen;
    private double _savedMainLeft;
    private double _savedMainTop;
    private WindowState _savedMainState = WindowState.Normal;
    private uint _lastElevatedQamHintPid;
    private nint _suspendResumeNotifyHandle;
    private nint _displayPowerNotifyHandle;
    private int _lastConsoleDisplayState = -1;
    private bool _sessionSwitchSubscribed;

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
        int captureDocsIdx = Array.FindIndex(cliArgs, a => string.Equals(a, "--capture-docs", StringComparison.OrdinalIgnoreCase));
        bool captureDocs = captureDocsIdx >= 0;
        string captureDocsOutDir = (captureDocs && captureDocsIdx + 1 < cliArgs.Length && !cliArgs[captureDocsIdx + 1].StartsWith("--", StringComparison.Ordinal))
            ? cliArgs[captureDocsIdx + 1]
            : Path.Combine(Environment.CurrentDirectory, "docs", "screenshots");
        bool startMinimized = cliArgs.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        bool startQam = cliArgs.Any(a => string.Equals(a, "--qam", StringComparison.OrdinalIgnoreCase));
        bool noAutoElevate = cliArgs.Any(a => string.Equals(a, "--no-auto-elevate", StringComparison.OrdinalIgnoreCase));
        int waitPidIdx = Array.FindIndex(cliArgs, a => string.Equals(a, "--relaunch-wait-pid", StringComparison.OrdinalIgnoreCase));
        int relaunchWaitPid = (waitPidIdx >= 0 && waitPidIdx + 1 < cliArgs.Length && int.TryParse(cliArgs[waitPidIdx + 1], out int parsedWaitPid))
            ? parsedWaitPid
            : 0;

        bool isVerifyMode = verifyStartup || verifyComponents || verifyGui || captureDocs;

        if (!isVerifyMode && relaunchWaitPid > 0 && relaunchWaitPid != Environment.ProcessId)
        {
            await WaitForPreviousInstanceExitAsync(relaunchWaitPid);
        }

        if (!isVerifyMode)
        {
            bool acquired = false;
            for (int attempt = 0; attempt < (relaunchWaitPid > 0 ? 25 : 1); attempt++)
            {
                _singleInstanceMutex = new Mutex(true, "HaloDeck.SingleInstance.Mutex", out bool isNew);
                if (isNew)
                {
                    acquired = true;
                    break;
                }
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                if (relaunchWaitPid > 0)
                {
                    await Task.Delay(120);
                }
            }

            if (!acquired)
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

        var settingsStore = (verifyStartup || verifyGui || captureDocs)
            ? new SettingsStore(Path.Combine(Path.GetTempPath(), $"HaloDeck_Verify_{Environment.ProcessId}"))
            : new SettingsStore();
        var settings = settingsStore.Load();

        if (!isVerifyMode && settings.RunAsAdministrator && !Win32Interop.IsCurrentProcessElevated() && !noAutoElevate)
        {
            if (TryRelaunchElevatedForStartup(startMinimized, startQam))
            {
                try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
                _singleInstanceMutex?.Dispose();
                _singleInstanceMutex = null;
                Shutdown(0);
                return;
            }
        }

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
        HandheldToolboxService.AttachInputPaneWatcher(_hiddenHotkeyHost.Handle);
        _inputHub.AttachHotkeyWindow(_hiddenHotkeyHost.Handle);
        if (!isVerifyMode)
        {
            _hiddenHotkeyHost.AddHook(PowerBroadcastWndProc);
            try
            {
                _suspendResumeNotifyHandle = Win32Interop.RegisterSuspendResumeNotification(
                    _hiddenHotkeyHost.Handle,
                    Win32Interop.DEVICE_NOTIFY_WINDOW_HANDLE);
            }
            catch { }
            try
            {
                var displayGuid = Win32Interop.GUID_CONSOLE_DISPLAY_STATE;
                _displayPowerNotifyHandle = Win32Interop.RegisterPowerSettingNotification(
                    _hiddenHotkeyHost.Handle,
                    ref displayGuid,
                    Win32Interop.DEVICE_NOTIFY_WINDOW_HANDLE);
            }
            catch { }
            try
            {
                SystemEvents.SessionSwitch += OnSystemSessionSwitch;
                _sessionSwitchSubscribed = true;
            }
            catch { }
        }

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

        if (captureDocs)
        {
            _bridge.SuppressHardwareCleanupOnDispose = true;
            int exitCode = await RunCaptureDocsAsync(captureDocsOutDir, _mainWindow, _qamWindow, _bridge);
            ShutdownApplicationWithCode(exitCode);
            return;
        }

        ApplyOverlayHudFromSettings();
        InstallTopmostGuardianHooks();
        _inputHub.StartPolling();

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _pollTimer.Tick += (_, _) =>
        {
            if (Interlocked.CompareExchange(ref _pollInFlight, 1, 0) != 0)
            {
                return;
            }

            _pollCounter++;
            bool runAutoInject = _pollCounter % 19 == 0;
            bool uiVisible = (_mainWindow?.IsVisible == true && !_isConsoleClosingShieldActive) ||
                             (_qamWindow?.IsVisible == true);
            bool osdVisible = _bridge.Settings.OverlayLevel > 0;

            _ = Task.Run(() =>
            {
                try
                {
                    _bridge.PollRunningAndAutoInjectTick(runAutoInject, uiVisible, osdVisible);
                    if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished || _bridge is null) return;

                        var latestSys = _bridge.LatestSystemState;
                        if (_overlayHudWindow is not null)
                        {
                            UpdateOverlayPresentation(latestSys, settingsChanged: false, checkZOrderCover: (_pollCounter & 1) == 0);
                        }

                        if ((_mainWindow?.IsVisible == true) || (_qamWindow?.IsVisible == true))
                        {
                            _bridge.BroadcastLatestTelemetry();
                        }
                        UpdateUiActiveFlag();
                    });
                }
                catch
                {
                    // Ignore transient background telemetry exceptions
                }
                finally
                {
                    Interlocked.Exchange(ref _pollInFlight, 0);
                }
            });
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

    private static async Task WaitForPreviousInstanceExitAsync(int pid)
    {
        var deadline = DateTime.UtcNow.AddSeconds(4.5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                if (proc.HasExited) return;
            }
            catch
            {
                return;
            }
            await Task.Delay(80);
        }
    }

    private static bool TryRelaunchElevatedForStartup(bool startMinimized, bool startQam)
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return false;

            var args = new List<string> { "--relaunch-wait-pid", Environment.ProcessId.ToString(), "--no-auto-elevate" };
            if (startMinimized) args.Add("--minimized");
            if (startQam) args.Add("--qam");

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = string.Join(' ', args),
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch
        {
            return false;
        }
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
                _inputHub.SuppressUntilGamepadRelease();
                await _qamWindow.ToggleOverlayAsync();
                UpdateUiActiveFlag();
                ScheduleOverlayTopmostBurst(forceReband: true);
            });
        };

        _inputHub.ToggleConsoleRequested += () =>
        {
            Dispatcher.InvokeAsync(ToggleMainConsole);
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

        _inputHub.ToggleOnScreenKeyboardRequested += forceClose =>
        {
            Dispatcher.InvokeAsync(async () =>
            {
                _bridge?.TriggerPhysicalOskToggle(forceClose);
                UpdateUiActiveFlag();
                await Task.Delay(250);
                if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                {
                    UpdateUiActiveFlag();
                }
            });
        };

        _bridge.OverlayHudUpdated += () =>
        {
            Dispatcher.Invoke(() =>
            {
                ApplyOverlayHudFromSettings();
                ScheduleOverlayTopmostBurst(forceReband: true);
            });
        };

        _bridge.RequestHideQam += () =>
        {
            Dispatcher.InvokeAsync(async () =>
            {
                _inputHub?.SuppressUntilGamepadRelease();
                await _qamWindow.HideAndRestoreGameFocusAsync();
                UpdateUiActiveFlag();
                ScheduleOverlayTopmostBurst(forceReband: true);
            });
        };

        _bridge.RequestHideQamAsync += async () =>
        {
            _inputHub?.SuppressUntilGamepadRelease();
            await _qamWindow.HideAndRestoreGameFocusAsync();
            UpdateUiActiveFlag();
            ScheduleOverlayTopmostBurst(forceReband: true);
        };

        _bridge.RequestToggleQam += () =>
        {
            Dispatcher.InvokeAsync(async () =>
            {
                _inputHub?.SuppressUntilGamepadRelease();
                await _qamWindow.ToggleOverlayAsync();
                UpdateUiActiveFlag();
                ScheduleOverlayTopmostBurst(forceReband: true);
            });
        };

        _bridge.RequestShowConsole += ShowOrActivateMainConsole;
        _bridge.RequestToggleFullscreen += () => _mainWindow.ToggleFullscreen();
        _bridge.RequestRepositionQam += () => Dispatcher.Invoke(() => _qamWindow?.RepositionOnRightEdge());
        _bridge.RequestMinimizeToTray += HideMainConsoleAndRestoreGame;
        _bridge.RequestExitForRelaunch += () =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_bridge is not null) _bridge.SuppressHardwareCleanupOnDispose = true;
                ShutdownApplication();
            });
        };
        _mainWindow.CloseToTrayRequested += HideMainConsoleAndRestoreGame;
        _qamWindow.ClosingShieldChanged += () =>
        {
            UpdateUiActiveFlag();
            ScheduleOverlayTopmostBurst(forceReband: true);
        };

        _mainWindow.Activated += (_, _) => UpdateUiActiveFlag();
        _mainWindow.Deactivated += (_, _) =>
        {
            UpdateUiActiveFlag();
            ScheduleOverlayTopmostBurst(forceReband: true);
        };
        _mainWindow.IsVisibleChanged += (_, _) =>
        {
            UpdateUiActiveFlag();
            ScheduleOverlayTopmostBurst(forceReband: true);
        };
        _qamWindow.Activated += (_, _) => UpdateUiActiveFlag();
        _qamWindow.Deactivated += (_, _) =>
        {
            UpdateUiActiveFlag();
            Dispatcher.BeginInvoke(CheckAutoDismissQamOnExternalFocus);
        };
        _qamWindow.IsVisibleChanged += (_, _) =>
        {
            UpdateUiActiveFlag();
            ScheduleOverlayTopmostBurst(forceReband: true);
        };
    }

    private void CaptureExternalForegroundWindow()
    {
        var fg = Win32Interop.GetForegroundWindow();
        if (fg == 0 || !Win32Interop.IsWindow(fg)) return;

        Win32Interop.GetWindowThreadProcessId(fg, out uint fgPid);
        if (fgPid == (uint)Environment.ProcessId) return;

        _lastExternalForegroundHwnd = fg;
    }

    private void RestoreMainBoundsIfParked()
    {
        if (_mainWindow is null || !_mainWasParkedOffscreen) return;
        _mainWasParkedOffscreen = false;
        _mainWindow.Left = _savedMainLeft;
        _mainWindow.Top = _savedMainTop;
        if (_savedMainState == WindowState.Maximized)
        {
            _mainWindow.WindowState = WindowState.Maximized;
        }
    }

    private void ToggleMainConsole()
    {
        if (_mainWindow is { IsVisible: true } && !_isConsoleClosingShieldActive &&
            (_mainWindow.IsActive || Win32Interop.IsWindowOrChildForeground(new WindowInteropHelper(_mainWindow).Handle)))
        {
            HideMainConsoleAndRestoreGame();
            return;
        }
        ShowOrActivateMainConsole();
    }

    private void ShowOrActivateMainConsole()
    {
        if (_mainWindow is null) return;
        ++_consoleCloseVersion;
        _isConsoleClosingShieldActive = false;
        RestoreMainBoundsIfParked();
        _mainWindow.Opacity = 1.0;
        _mainWindow.IsHitTestVisible = true;
        _inputHub?.SuppressUntilGamepadRelease();

        CaptureExternalForegroundWindow();
        if ((_lastExternalForegroundHwnd == 0 || !Win32Interop.IsWindow(_lastExternalForegroundHwnd)) &&
            _qamWindow is { PreviousForegroundHwnd: not 0 } &&
            Win32Interop.IsWindow(_qamWindow.PreviousForegroundHwnd))
        {
            _lastExternalForegroundHwnd = _qamWindow.PreviousForegroundHwnd;
        }

        _qamWindow?.HideWithoutRestoringGame(restoreController: false);
        _bridge?.MuteActiveGameControllerInput(_lastExternalForegroundHwnd);

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }
        _mainWindow.Activate();
        var mainHwnd = new WindowInteropHelper(_mainWindow).Handle;
        Win32Interop.ForceForegroundWindow(mainHwnd, makeTopmost: false);
        _mainWindow.ConsoleWebView.Focus();
        UpdateUiActiveFlag();
    }

    private void HideMainConsoleAndRestoreGame()
    {
        _ = HideMainConsoleAndRestoreGameAsync();
    }

    private async Task HideMainConsoleAndRestoreGameAsync(
        Func<bool>? isButtonHeldOverride = null,
        bool forceShield = false)
    {
        if (_mainWindow is null || !_mainWindow.IsVisible || _isConsoleClosingShieldActive) return;

        int ver = ++_consoleCloseVersion;
        _inputHub?.SuppressUntilGamepadRelease();
        var holdCheck = isButtonHeldOverride ?? (() =>
            XInputInterop.HasAnyActiveButtonOrTrigger() ||
            (Win32Interop.GetAsyncKeyState(0x1B /* VK_ESCAPE */) & 0x8000) != 0);

        bool shouldShield = forceShield || FrontendFocusArbitrator.ShouldEngageCloseFocusShield(
            holdCheck(),
            XInputInterop.MillisecondsSinceLastButtonOrTrigger(),
            (Win32Interop.GetAsyncKeyState(0x1B) & 0x8000) != 0);

        if (shouldShield)
        {
            _isConsoleClosingShieldActive = true;
            _mainWindow.IsHitTestVisible = false;

            if (!_mainWasParkedOffscreen)
            {
                _savedMainState = _mainWindow.WindowState;
                if (_mainWindow.WindowState == WindowState.Maximized)
                {
                    _mainWindow.WindowState = WindowState.Normal;
                }
                _savedMainLeft = _mainWindow.Left;
                _savedMainTop = _mainWindow.Top;
                _mainWasParkedOffscreen = true;
            }
            _mainWindow.Left = -32000;
            _mainWindow.Top = -32000;
            var mainHwnd = new WindowInteropHelper(_mainWindow).Handle;
            Win32Interop.ParkWindowOffscreenNoActivate(mainHwnd);
            UpdateUiActiveFlag();

            var maxDeadline = DateTime.UtcNow.AddMilliseconds(FrontendFocusArbitrator.MaxCloseReleaseWaitMs);
            int cleanMs = 0;
            while (DateTime.UtcNow < maxDeadline && ver == _consoleCloseVersion)
            {
                if (holdCheck())
                {
                    cleanMs = 0;
                }
                else
                {
                    cleanMs += 10;
                    if (cleanMs >= FrontendFocusArbitrator.PostReleaseDrainMs)
                    {
                        break;
                    }
                }
                await Task.Delay(10);
            }
            if (ver != _consoleCloseVersion) return;
        }

        _isConsoleClosingShieldActive = false;
        _mainWindow.Opacity = 1.0;
        _mainWindow.IsHitTestVisible = true;
        _mainWindow.Hide();
        RestoreMainBoundsIfParked();
        _bridge?.RestoreMutedGameControllerInput();

        if (_lastExternalForegroundHwnd != 0 && Win32Interop.IsWindow(_lastExternalForegroundHwnd))
        {
            Win32Interop.RestoreWindowFocusIfValid(_lastExternalForegroundHwnd);
        }

        UpdateUiActiveFlag();
        ScheduleOverlayTopmostBurst(forceReband: true);
    }

    private static bool IsTouchKeyboardForegroundWindow(nint hwnd, uint pid)
    {
        try
        {
            if (hwnd != 0)
            {
                var sb = new System.Text.StringBuilder(128);
                int len = Win32Interop.GetClassNameW(hwnd, sb, sb.Capacity);
                if (len > 0)
                {
                    string className = sb.ToString(0, len);
                    if (string.Equals(className, "IPTip_Main_Window", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Ignore transient window class query failures
        }

        if (pid != 0 && pid <= int.MaxValue)
        {
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                string procName = proc.ProcessName;
                if (string.Equals(procName, "TextInputHost", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(procName, "TabTip", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore exited or inaccessible process
            }
        }

        return false;
    }

    private void CheckAutoDismissQamOnExternalFocus()
    {
        if (_qamWindow is null || !_qamWindow.IsInteractiveVisible) return;

        nint fg = Win32Interop.GetForegroundWindow();
        if (fg == 0) return;

        nint qamHwnd = new WindowInteropHelper(_qamWindow).Handle;
        if (fg == qamHwnd || Win32Interop.IsChild(qamHwnd, fg)) return;

        Win32Interop.GetWindowThreadProcessId(fg, out uint fgPid);
        bool tabTipVisible = HandheldToolboxService.QueryTabTipVisible() ||
                             IsTouchKeyboardForegroundWindow(fg, fgPid);
        double msSinceShown = (DateTime.UtcNow - _qamWindow.ShownAtUtc).TotalMilliseconds;
        bool selfElevated = Win32Interop.IsCurrentProcessElevated();
        bool fgElevated = !selfElevated && fgPid != 0 && fgPid != (uint)Environment.ProcessId && Win32Interop.IsProcessElevated(fgPid);

        if (fgElevated && _lastElevatedQamHintPid != fgPid)
        {
            _lastElevatedQamHintPid = fgPid;
            _bridge?.NotifyToast("warn", "检测到当前游戏以管理员权限运行，建议在「手柄与设置」点击「立即提权重启」以获得完整焦点与 Insert 按键穿透");
        }

        if (FrontendFocusArbitrator.ShouldAutoDismissQamOnExternalForeground(
                _qamWindow.IsInteractiveVisible,
                msSinceShown,
                fg,
                fgPid,
                (uint)Environment.ProcessId,
                tabTipVisible,
                externalForegroundIsElevated: fgElevated,
                selfIsElevated: selfElevated))
        {
            _qamWindow.HideWithoutRestoringGame();
            UpdateUiActiveFlag();
            ScheduleOverlayTopmostBurst(forceReband: true);
        }
    }

    private void EnforceOverlayZOrder(bool forceReband = false)
    {
        nint hudHwnd = (_bridge?.Settings.OverlayLevel > 0 && _overlayHudWindow is { IsVisible: true })
            ? new WindowInteropHelper(_overlayHudWindow).Handle
            : 0;

        bool tabTipOpen = HandheldToolboxService.ShouldCloseFromHookSnapshotFast();
        if (_qamWindow is { IsInteractiveVisible: true } && !tabTipOpen)
        {
            _qamWindow.EnsureTopmostAboveGame(hudHwnd, forceReband);
        }

        if (_bridge?.Settings.OverlayLevel > 0 &&
            string.Equals(_bridge.ActiveOverlayBackend, RtssOsdFormatter.ModeNative, StringComparison.Ordinal))
        {
            _overlayHudWindow?.EnsureTopmostZOrder(forceReband);
        }
    }

    private void ScheduleOverlayTopmostBurst(bool forceReband = false)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

        EnforceOverlayZOrder(forceReband);
        foreach (int delayMs in new[] { 45, 130, 300 })
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
                try
                {
                    await Dispatcher.InvokeAsync(() => EnforceOverlayZOrder(false));
                }
                catch
                {
                    // Ignore during shutdown
                }
            });
        }
    }

    private void InstallTopmostGuardianHooks()
    {
        _topmostGuardTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _topmostGuardTimer.Tick -= OnTopmostGuardTick;
        _topmostGuardTimer.Tick += OnTopmostGuardTick;
        _topmostGuardTimer.Start();

        _winEventProc ??= OnSystemWinEvent;
        if (_winEventHookForeground == 0)
        {
            _winEventHookForeground = Win32Interop.SetWinEventHook(
                Win32Interop.EVENT_SYSTEM_FOREGROUND,
                Win32Interop.EVENT_SYSTEM_FOREGROUND,
                0,
                _winEventProc,
                0,
                0,
                Win32Interop.WINEVENT_OUTOFCONTEXT);
        }
        if (_winEventHookMinimizeEnd == 0)
        {
            _winEventHookMinimizeEnd = Win32Interop.SetWinEventHook(
                Win32Interop.EVENT_SYSTEM_MINIMIZEEND,
                Win32Interop.EVENT_SYSTEM_MINIMIZEEND,
                0,
                _winEventProc,
                0,
                0,
                Win32Interop.WINEVENT_OUTOFCONTEXT);
        }
    }

    private void OnTopmostGuardTick(object? sender, EventArgs e)
    {
        bool interactiveUiOpen = (_mainWindow is { IsVisible: true } && !_isConsoleClosingShieldActive) ||
                                 (_qamWindow is { IsInteractiveVisible: true }) ||
                                 (_inputHub?.IsUiActive == true);
        if (!interactiveUiOpen)
        {
            return;
        }

        CheckAutoDismissQamOnExternalFocus();
        if (_qamWindow is { IsInteractiveVisible: true })
        {
            EnforceOverlayZOrder(forceReband: false);
        }
        UpdateUiActiveFlag();
    }

    private void OnSystemWinEvent(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        Dispatcher.BeginInvoke(() =>
        {
            CheckAutoDismissQamOnExternalFocus();
            ScheduleOverlayTopmostBurst(forceReband: false);
            UpdateUiActiveFlag();
        });
    }

    private void UpdateUiActiveFlag()
    {
        if (_inputHub is null) return;
        nint fg = Win32Interop.GetForegroundWindow();
        bool desktopHasNoForeground = fg == 0;

        bool qamInteractive = _qamWindow is { IsInteractiveVisible: true };
        nint qamHwnd = qamInteractive ? new WindowInteropHelper(_qamWindow!).Handle : 0;
        bool qamFocused = qamInteractive &&
                          (_qamWindow!.IsActive || Win32Interop.IsWindowOrChildForeground(qamHwnd));
        bool qamFocusBlockedByElevation = false;
        if (qamInteractive && !qamFocused && fg != 0 && !Win32Interop.IsCurrentProcessElevated())
        {
            Win32Interop.GetWindowThreadProcessId(fg, out uint fgPid);
            if (fgPid != 0 && fgPid != (uint)Environment.ProcessId && Win32Interop.IsProcessElevated(fgPid))
            {
                qamFocusBlockedByElevation = true;
            }
        }

        bool mainVisible = _mainWindow is { IsVisible: true } && !_isConsoleClosingShieldActive;
        nint mainHwnd = mainVisible ? new WindowInteropHelper(_mainWindow!).Handle : 0;
        bool mainFocused = mainVisible &&
                           (_mainWindow!.IsActive || Win32Interop.IsWindowOrChildForeground(mainHwnd));

        bool tabTipVisible = HandheldToolboxService.ShouldCloseFromHookSnapshotFast();

        string? nextTarget = FrontendFocusArbitrator.ResolveActiveUiTarget(
            qamInteractive,
            qamFocused,
            mainVisible,
            mainFocused,
            desktopHasNoForeground,
            qamFocusBlockedByElevation,
            tabTipVisible: tabTipVisible);

        _inputHub.ActiveUiTarget = nextTarget;

        if (nextTarget is not null || (tabTipVisible && (qamInteractive || mainVisible)))
        {
            nint targetGameHwnd = qamInteractive && _qamWindow is { PreviousForegroundHwnd: not 0 }
                ? _qamWindow.PreviousForegroundHwnd
                : _lastExternalForegroundHwnd;
            _bridge?.MuteActiveGameControllerInput(targetGameHwnd);
        }
        else if (_qamWindow is not { IsClosingShieldActive: true } && !_isConsoleClosingShieldActive)
        {
            _bridge?.RestoreMutedGameControllerInput();
        }
    }

    private void ApplyOverlayHudFromSettings()
    {
        if (_bridge is null || _overlayHudWindow is null) return;
        UpdateOverlayPresentation(_bridge.LatestSystemState, settingsChanged: true, checkZOrderCover: false);
    }

    private void UpdateOverlayPresentation(SystemControlState sys, bool settingsChanged, bool checkZOrderCover)
    {
        if (_bridge is null || _overlayHudWindow is null) return;
        var s = _bridge.Settings;

        if (s.OverlayLevel <= 0)
        {
            _bridge.ActiveOverlayBackend = "off";
            _bridge.RtssClient.ClearOsd();
            if (settingsChanged || _overlayHudWindow.IsVisible)
            {
                _overlayHudWindow.ApplySettings(0, s.OverlayPosition, s.OverlayOpacity);
            }
            return;
        }

        string mode = RtssOsdFormatter.NormalizeRendererMode(s.OverlayRenderer);
        bool rtssRunning = mode != RtssOsdFormatter.ModeNative &&
                           _bridge.RtssClient.IsRtssRunning(forceProbe: settingsChanged);
        bool exclusiveFs = mode == RtssOsdFormatter.ModeAutoRtss &&
                           rtssRunning &&
                           Win32Interop.IsD3DExclusiveFullscreenActive();
        string backend = RtssOsdFormatter.ResolveEffectiveBackend(mode, s.OverlayLevel, rtssRunning, exclusiveFs);
        bool backendChanged = !string.Equals(_bridge.ActiveOverlayBackend, backend, StringComparison.Ordinal);
        _bridge.ActiveOverlayBackend = backend;
        sys.RtssRunning = rtssRunning;
        sys.ActiveOverlayBackend = backend;

        if (backend == RtssOsdFormatter.ModeRtss)
        {
            _overlayHudWindow.HideTemporarilyForExternalRenderer();
            int shownTargetWatts = sys.HardwareService.ServiceRunning && sys.HardwareService.AppliedTdpWatts > 0
                ? sys.HardwareService.AppliedTdpWatts
                : s.CurrentTdpWatts;
            var metrics = new RtssOsdMetrics(
                CurrentFps: sys.CurrentFps,
                FrameTimeMs: sys.FrameTimeMs,
                ApuPowerWatts: sys.ApuPowerWatts,
                TargetTdpWatts: shownTargetWatts,
                GpuUsagePercent: sys.GpuUsagePercent,
                GfxClockMhz: sys.GfxClockMhz,
                GpuTempC: sys.GpuTempC,
                CpuUsagePercent: sys.CpuUsagePercent,
                CpuClockMhz: sys.CpuClockMhz,
                CpuTempC: sys.CpuTempC,
                VramUsedMb: sys.VramUsedMb,
                VramTotalMb: sys.VramTotalMb,
                UsedRamGb: sys.UsedRamGb,
                FanRpm: sys.HardwareService.EcReady ? sys.HardwareService.FanRpm : 0,
                HasBattery: sys.HasBattery,
                BatteryPercent: sys.BatteryPercent,
                SystemDrawWatts: sys.SystemDrawWatts,
                PowerRuntimeMode: sys.PowerRuntimeMode,
                BatteryDischargeWatts: sys.BatteryDischargeWatts,
                BatteryPowerWatts: sys.BatteryPowerWatts,
                EstimatedStandbyMinutes: sys.EstimatedStandbyMinutes,
                Position: s.OverlayPosition,
                Opacity: s.OverlayOpacity);

            var payload = RtssOsdFormatter.BuildPayload(s.OverlayLevel, metrics);
            if (_bridge.RtssClient.UpdateOsd(payload.Text, payload.EmbeddedObjectBuffer))
            {
                return;
            }

            // RTSS exited mid-frame: gracefully fall back to native WPF overlay
            _bridge.ActiveOverlayBackend = RtssOsdFormatter.ModeNative;
            sys.RtssRunning = false;
            sys.ActiveOverlayBackend = RtssOsdFormatter.ModeNative;
            backendChanged = true;
        }
        else
        {
            _bridge.RtssClient.ClearOsd();
        }

        if (settingsChanged || backendChanged || !_overlayHudWindow.IsVisible)
        {
            _overlayHudWindow.ApplySettings(s.OverlayLevel, s.OverlayPosition, s.OverlayOpacity);
        }
        _overlayHudWindow.UpdateMetrics(sys, _bridge.RunningGame, s.CurrentTdpWatts);

        if (checkZOrderCover && _overlayHudWindow.IsVisible)
        {
            nint hudHwnd = new WindowInteropHelper(_overlayHudWindow).Handle;
            nint qamHwnd = _qamWindow is { IsVisible: true } ? new WindowInteropHelper(_qamWindow).Handle : 0;
            if (Win32Interop.HasLightweightTopmostCover(hudHwnd, qamHwnd))
            {
                _overlayHudWindow.EnsureTopmostZOrder(forceReband: false);
            }
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
        _topmostGuardTimer?.Stop();
        if (_winEventHookForeground != 0)
        {
            Win32Interop.UnhookWinEvent(_winEventHookForeground);
            _winEventHookForeground = 0;
        }
        if (_winEventHookMinimizeEnd != 0)
        {
            Win32Interop.UnhookWinEvent(_winEventHookMinimizeEnd);
            _winEventHookMinimizeEnd = 0;
        }
        _winEventProc = null;
        HandheldToolboxService.DetachInputPaneWatcher();
        _inputHub?.Dispose();
        if (_suspendResumeNotifyHandle != 0)
        {
            try { Win32Interop.UnregisterSuspendResumeNotification(_suspendResumeNotifyHandle); } catch { }
            _suspendResumeNotifyHandle = 0;
        }
        if (_displayPowerNotifyHandle != 0)
        {
            try { Win32Interop.UnregisterPowerSettingNotification(_displayPowerNotifyHandle); } catch { }
            _displayPowerNotifyHandle = 0;
        }
        if (_sessionSwitchSubscribed)
        {
            try { SystemEvents.SessionSwitch -= OnSystemSessionSwitch; } catch { }
            _sessionSwitchSubscribed = false;
        }
        _hiddenHotkeyHost?.RemoveHook(PowerBroadcastWndProc);
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

    private bool ShouldSuppressLegacyOsk()
    {
        var settings = _bridge?.Settings;
        return settings is not null &&
               settings.InterceptPhysicalOskButton &&
               !string.Equals((settings.PreferredOskMode ?? "tabtip").Trim(), "osk", StringComparison.OrdinalIgnoreCase);
    }

    private void OnSystemSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            if (ShouldSuppressLegacyOsk())
            {
                HandheldToolboxService.ClearLegacyOskPersistenceState();
                HandheldToolboxService.TryDismissLegacyOsk();
            }
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            _inputHub?.ReinstallKeyboardHook();
            if (ShouldSuppressLegacyOsk())
            {
                _ = HandheldToolboxService.SuppressLegacyOskAcrossSleepWakeAsync(shouldSuppress: true);
            }
        }
    }

    private nint PowerBroadcastWndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != Win32Interop.WM_POWERBROADCAST)
        {
            return 0;
        }

        uint wp = unchecked((uint)wParam.ToInt64());
        if (wp == Win32Interop.PBT_APMSUSPEND)
        {
            if (ShouldSuppressLegacyOsk())
            {
                HandheldToolboxService.ClearLegacyOskPersistenceState();
                HandheldToolboxService.TryDismissLegacyOsk();
            }
            return 0;
        }

        if (wp is Win32Interop.PBT_APMRESUMESUSPEND
            or Win32Interop.PBT_APMRESUMEAUTOMATIC
            or Win32Interop.PBT_APMRESUMECRITICAL)
        {
            _inputHub?.ReinstallKeyboardHook();
            _bridge?.NotifySystemPowerResume($"WM_POWERBROADCAST:0x{wp:X}");
            if (ShouldSuppressLegacyOsk())
            {
                _ = HandheldToolboxService.SuppressLegacyOskAcrossSleepWakeAsync(shouldSuppress: true);
            }
        }
        else if (wp == Win32Interop.PBT_POWERSETTINGCHANGE && lParam != 0)
        {
            try
            {
                var setting = System.Runtime.InteropServices.Marshal.PtrToStructure<Win32Interop.POWERBROADCAST_SETTING>(lParam);
                if (setting.PowerSetting == Win32Interop.GUID_CONSOLE_DISPLAY_STATE && setting.DataLength >= 1)
                {
                    int state = setting.Data;
                    if (TdpLockPolicy.IsDisplayWakeTransition(_lastConsoleDisplayState, state))
                    {
                        _inputHub?.ReinstallKeyboardHook();
                        _bridge?.NotifySystemPowerResume("DisplayWake");
                        if (ShouldSuppressLegacyOsk())
                        {
                            _ = HandheldToolboxService.SuppressLegacyOskAcrossSleepWakeAsync(shouldSuppress: true);
                        }
                    }
                    _lastConsoleDisplayState = state;
                }
            }
            catch
            {
                // Ignore malformed power setting payloads
            }
        }

        return 0;
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

    private static async Task PrepareCleanCaptureAsync(CoreWebView2 core)
    {
        await core.ExecuteScriptAsync("""
            (() => {
              const ts = document.getElementById('toastStack');
              if (ts) ts.innerHTML = '';
              document.querySelectorAll('.gp-focused').forEach(el => el.classList.remove('gp-focused'));
              const mv = document.getElementById('mainView');
              if (mv) mv.scrollTop = 0;
              const ps = document.querySelector('.page-scroll');
              if (ps) ps.scrollTop = 0;
            })()
            """);
        await Task.Delay(120);
    }

    private static async Task<int> RunCaptureDocsAsync(
        string outputDir,
        MainWindow mainWindow,
        QamWindow qamWindow,
        WebUiBridge bridge)
    {
        Directory.CreateDirectory(outputDir);
        var errLog = Path.Combine(outputDir, "_capture_error.log");
        if (File.Exists(errLog)) File.Delete(errLog);

        try
        {
            mainWindow.Show();
            mainWindow.Activate();
            await mainWindow.EnsureInitializedOffscreenForVerifyAsync();

            var mainCore = mainWindow.ConsoleWebView.CoreWebView2
                           ?? throw new InvalidOperationException("Main CoreWebView2 未初始化");

            bool handheldReady = await WaitUntilAsync(
                mainCore,
                "document.readyState === 'complete' && Boolean(document.querySelector('.tab-btn[data-nav=\"handheld\"].active')) && document.querySelectorAll('[data-tdp-watt]').length >= 3 && Boolean(document.getElementById('pfSysVal')?.textContent?.trim())");
            if (!handheldReady)
            {
                File.WriteAllText(errLog, "console-handheld did not become ready in time.");
                return 1;
            }
            bridge.BroadcastLatestTelemetry();
            await Task.Delay(450);
            await PrepareCleanCaptureAsync(mainCore);
            await CapturePngAsync(mainCore, outputDir, "console-handheld.png");

            await mainCore.ExecuteScriptAsync("(() => { if ((document.body.dataset.layout || 'bento') !== 'cockpit') document.getElementById('btnLayoutSwitch')?.click(); })()");
            await Task.Delay(250);
            await PrepareCleanCaptureAsync(mainCore);
            await CapturePngAsync(mainCore, outputDir, "console-cockpit.png");
            await mainCore.ExecuteScriptAsync("(() => { if ((document.body.dataset.layout || 'bento') !== 'bento') document.getElementById('btnLayoutSwitch')?.click(); })()");
            await Task.Delay(200);

            await mainCore.ExecuteScriptAsync("document.querySelector('.tab-btn[data-nav=\"monitor\"]').click()");
            bool monReady = await WaitUntilAsync(
                mainCore,
                "document.querySelector('.tab-btn[data-nav=\"monitor\"]')?.classList.contains('active') && document.querySelectorAll('svg.sparkline-svg').length === 2 && Boolean(document.getElementById('kpiFps')?.textContent?.trim())");
            if (!monReady)
            {
                File.WriteAllText(errLog, "console-monitor timeout");
                return 1;
            }
            bridge.BroadcastLatestTelemetry();
            await Task.Delay(350);
            await PrepareCleanCaptureAsync(mainCore);
            await CapturePngAsync(mainCore, outputDir, "console-monitor.png");

            await mainCore.ExecuteScriptAsync("document.querySelector('.tab-btn[data-nav=\"controls\"]').click()");
            bool ctrlReady = await WaitUntilAsync(
                mainCore,
                "document.querySelector('.tab-btn[data-nav=\"controls\"]')?.classList.contains('active') && Boolean(document.getElementById('btnPickL4'))");
            if (!ctrlReady)
            {
                File.WriteAllText(errLog, "console-controls timeout");
                return 1;
            }
            await Task.Delay(300);
            await PrepareCleanCaptureAsync(mainCore);
            await CapturePngAsync(mainCore, outputDir, "console-controls.png");

            await mainCore.ExecuteScriptAsync("document.querySelector('.tab-btn[data-nav=\"opti\"]').click()");
            bool optiReady = await WaitUntilAsync(
                mainCore,
                "document.querySelector('.tab-btn[data-nav=\"opti\"]')?.classList.contains('active') && document.querySelectorAll('.preset-card').length === 4");
            if (!optiReady)
            {
                File.WriteAllText(errLog, "console-opti timeout");
                return 1;
            }
            await Task.Delay(400);
            await PrepareCleanCaptureAsync(mainCore);
            await CapturePngAsync(mainCore, outputDir, "console-opti.png");

            await mainCore.ExecuteScriptAsync("document.querySelector('.sub-tab-btn[data-opti-sub=\"library\"]').click()");
            bool libReady = await WaitUntilAsync(
                mainCore,
                "document.querySelector('.sub-tab-btn[data-opti-sub=\"library\"]')?.classList.contains('active') && document.querySelectorAll('.poster-grid').length === 1");
            if (!libReady)
            {
                File.WriteAllText(errLog, "console-library timeout");
                return 1;
            }
            await Task.Delay(500);
            await PrepareCleanCaptureAsync(mainCore);
            await CapturePngAsync(mainCore, outputDir, "console-library.png");

            await mainCore.ExecuteScriptAsync("document.querySelector('.tab-btn[data-nav=\"handheld\"]').click()");
            await WaitUntilAsync(mainCore, "document.querySelectorAll('[data-tdp-watt]').length >= 3");
            await mainCore.ExecuteScriptAsync("document.getElementById('btnOpenQam').click()");

            var qamDeadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < qamDeadline && !(qamWindow.IsVisible && qamWindow.WebViewInitialized))
            {
                await Task.Delay(100);
            }
            if (!(qamWindow.IsVisible && qamWindow.WebViewInitialized) || qamWindow.QamWebView.CoreWebView2 is not { } qamCore)
            {
                File.WriteAllText(errLog, "QAM WebView2 failed to initialize.");
                return 1;
            }

            bool qamHandheldReady = await WaitUntilAsync(
                qamCore,
                "document.body.classList.contains('mode-qam') && document.querySelector('.qam-rail-btn[data-qam-tab=\"handheld\"]')?.classList.contains('active') && Boolean(document.getElementById('tdpRangeInput'))");
            if (!qamHandheldReady)
            {
                File.WriteAllText(errLog, "qam-handheld timeout");
                return 1;
            }
            bridge.BroadcastLatestTelemetry();
            await Task.Delay(350);
            await PrepareCleanCaptureAsync(qamCore);
            await CapturePngAsync(qamCore, outputDir, "qam-handheld.png");

            await qamCore.ExecuteScriptAsync("document.querySelector('.qam-rail-btn[data-qam-tab=\"osd\"]').click()");
            bool qamOsdReady = await WaitUntilAsync(
                qamCore,
                "document.querySelector('.qam-rail-btn[data-qam-tab=\"osd\"]')?.classList.contains('active') && Boolean(document.querySelector('#qamOsdSparkline svg')) && Boolean(document.getElementById('qamKpiFps')?.textContent?.trim())");
            if (!qamOsdReady)
            {
                File.WriteAllText(errLog, "qam-osd timeout");
                return 1;
            }
            bridge.BroadcastLatestTelemetry();
            await Task.Delay(350);
            await PrepareCleanCaptureAsync(qamCore);
            await CapturePngAsync(qamCore, outputDir, "qam-osd.png");

            await qamCore.ExecuteScriptAsync("document.querySelector('.qam-rail-btn[data-qam-tab=\"controls\"]').click()");
            bool qamCtrlReady = await WaitUntilAsync(
                qamCore,
                "document.querySelector('.qam-rail-btn[data-qam-tab=\"controls\"]')?.classList.contains('active') && Boolean(document.getElementById('btnPickL4')) && Boolean(document.getElementById('swInterceptOsk'))");
            if (!qamCtrlReady)
            {
                File.WriteAllText(errLog, "qam-controls timeout");
                return 1;
            }
            await Task.Delay(350);
            await PrepareCleanCaptureAsync(qamCore);
            await CapturePngAsync(qamCore, outputDir, "qam-controls.png");

            await qamCore.ExecuteScriptAsync("document.querySelector('.qam-rail-btn[data-qam-tab=\"opti\"]').click()");
            bool qamOptiReady = await WaitUntilAsync(
                qamCore,
                "document.querySelector('.qam-rail-btn[data-qam-tab=\"opti\"]')?.classList.contains('active') && Boolean(document.getElementById('qamStatusBanner'))");
            if (!qamOptiReady)
            {
                File.WriteAllText(errLog, "qam-opti timeout");
                return 1;
            }
            await Task.Delay(350);
            await PrepareCleanCaptureAsync(qamCore);
            await CapturePngAsync(qamCore, outputDir, "qam-opti.png");

            qamWindow.Hide();
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(errLog, ex.ToString());
            return 1;
        }
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

            string origUiTheme = bridge.Settings.UiTheme;
            string origUiLayout = bridge.Settings.UiLayout;
            bool handheldReady = await WaitUntilAsync(
                mainCore,
                "document.readyState === 'complete' && Boolean(document.querySelector('.tab-btn[data-nav=\"handheld\"].active')) && document.querySelectorAll('[data-tdp-watt]').length >= 3 && document.querySelectorAll('[data-fan-mode]').length === 3 && document.querySelectorAll('[data-osd-level]').length === 5");
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

            // Capture all 4 (Layout x Theme) combinations on Page 1 & verify zero vertical scroll on both Bento and Cockpit
            async Task SwitchLayoutAndThemeAsync(string layoutId, string themeId)
            {
                await mainCore.ExecuteScriptAsync($$"""
                    (() => {
                      const curL = document.body.dataset.layout || 'bento';
                      if (curL !== '{{layoutId}}') document.getElementById('btnLayoutSwitch')?.click();
                    })()
                    """);
                await Task.Delay(160);
                await mainCore.ExecuteScriptAsync($$"""
                    (() => {
                      const curT = document.body.dataset.theme || 'aurora';
                      if (curT !== '{{themeId}}') document.getElementById('btnThemeSwitch')?.click();
                    })()
                    """);
                await Task.Delay(180);
                await mainCore.ExecuteScriptAsync("document.getElementById('toastStack').innerHTML = ''; document.querySelectorAll('.gp-focused').forEach(e => e.classList.remove('gp-focused'));");
                await Task.Delay(60);
            }

            await SwitchLayoutAndThemeAsync("bento", "aurora");
            await CapturePngAsync(mainCore, outputDir, "v5_01_bento_aurora.png");

            var homeInfoJson = await mainCore.ExecuteScriptAsync("""
                (() => {
                  const mv = document.getElementById('mainView');
                  return {
                    layout: document.body.dataset.layout || '',
                    theme: document.body.dataset.theme || '',
                    tdpChips: document.querySelectorAll('[data-tdp-watt]').length,
                    legacyTdpPresets: document.querySelectorAll('[data-tdp-preset]').length,
                    fanModes: document.querySelectorAll('[data-fan-mode]').length,
                    osdLevelsOnHome: document.querySelectorAll('[data-osd-level]').length,
                    closeBtnFits: Boolean(document.getElementById('btnCloseApp')) && document.getElementById('btnCloseApp').getBoundingClientRect().right <= window.innerWidth + 2,
                    hasPfSys: Boolean(document.getElementById('pfSysVal')?.textContent?.trim()),
                    hasPfEta: Boolean(document.getElementById('pfEtaVal')?.textContent?.trim()),
                    hasDrrPill: Boolean(document.querySelector('[data-refresh-hz="drr"]')),
                    activeRefreshVal: document.querySelector('[data-refresh-hz].active')?.dataset.refreshHz || '',
                    hasBrightRange: Boolean(document.getElementById('brightRange')),
                    hasVolRange: Boolean(document.getElementById('volRange')),
                    tdpRangeStep: document.getElementById('tdpRangeInput')?.getAttribute('step') || '',
                    tdpDecText: document.getElementById('btnTdpDec')?.textContent?.trim() || '',
                    tdpIncText: document.getElementById('btnTdpInc')?.textContent?.trim() || '',
                    toolboxTiles: document.querySelectorAll('.toolbox-tile').length,
                    cpuBoostPills: document.querySelectorAll('[data-cpu-boost]').length,
                    hasOfflineWifiBtn: Boolean(document.getElementById('btnToggleOfflineWifi')),
                    selectCount: document.querySelectorAll('select').length,
                    legendFits: document.getElementById('gamepadLegend').getBoundingClientRect().bottom <= window.innerHeight + 2,
                    noHorizOverflow: document.documentElement.scrollWidth <= document.documentElement.clientWidth + 2,
                    bentoZeroVertScroll: Boolean(mv) && mv.scrollHeight <= mv.clientHeight + 2
                  };
                })()
                """);
            using var homeDoc = JsonDocument.Parse(homeInfoJson);
            var homeRoot = homeDoc.RootElement;

            await SwitchLayoutAndThemeAsync("bento", "frost");
            await CapturePngAsync(mainCore, outputDir, "v5_02_bento_frost.png");

            await SwitchLayoutAndThemeAsync("cockpit", "aurora");
            await CapturePngAsync(mainCore, outputDir, "v7_03_cockpit_aurora.png");
            var cockpitScrollOkStr = await mainCore.ExecuteScriptAsync("(() => { const mv = document.getElementById('mainView'); return Boolean(mv) && mv.scrollHeight <= mv.clientHeight + 2 && document.querySelectorAll('.ck-cell').length === 6; })()");
            bool cockpitZeroVertScroll = string.Equals(cockpitScrollOkStr.Trim(), "true", StringComparison.OrdinalIgnoreCase);

            await SwitchLayoutAndThemeAsync("cockpit", "frost");
            await CapturePngAsync(mainCore, outputDir, "v7_04_cockpit_frost.png");

            await SwitchLayoutAndThemeAsync(origUiLayout, origUiTheme);

            var initSys = bridge.LatestSystemState;
            bool drrUiMatches = !initSys.DynamicRefreshSupported ||
                                (homeRoot.GetProperty("hasDrrPill").GetBoolean() &&
                                 (!initSys.IsDynamicRefreshRate || homeRoot.GetProperty("activeRefreshVal").GetString() == "drr"));
            bool homeOk = handheldReady &&
                          homeRoot.GetProperty("tdpChips").GetInt32() >= 3 &&
                          homeRoot.GetProperty("legacyTdpPresets").GetInt32() == 0 &&
                          homeRoot.GetProperty("fanModes").GetInt32() == 3 &&
                          homeRoot.GetProperty("osdLevelsOnHome").GetInt32() == 5 &&
                          homeRoot.GetProperty("closeBtnFits").GetBoolean() &&
                          homeRoot.GetProperty("hasPfSys").GetBoolean() &&
                          homeRoot.GetProperty("hasPfEta").GetBoolean() &&
                          drrUiMatches &&
                          homeRoot.GetProperty("hasBrightRange").GetBoolean() &&
                          homeRoot.GetProperty("hasVolRange").GetBoolean() &&
                          homeRoot.GetProperty("tdpRangeStep").GetString() == "1" &&
                          homeRoot.GetProperty("tdpDecText").GetString() == "−1W" &&
                          homeRoot.GetProperty("tdpIncText").GetString() == "+1W" &&
                          homeRoot.GetProperty("toolboxTiles").GetInt32() >= 8 &&
                          homeRoot.GetProperty("cpuBoostPills").GetInt32() == 3 &&
                          homeRoot.GetProperty("hasOfflineWifiBtn").GetBoolean() &&
                          homeRoot.GetProperty("selectCount").GetInt32() == 0 &&
                          homeRoot.GetProperty("legendFits").GetBoolean() &&
                          homeRoot.GetProperty("noHorizOverflow").GetBoolean() &&
                          homeRoot.GetProperty("bentoZeroVertScroll").GetBoolean() &&
                          cockpitZeroVertScroll;
            Record("handheldHomeDefault", homeOk, new
            {
                home = homeRoot.Clone(),
                cockpitZeroVertScroll,
                sysIsDrr = initSys.IsDynamicRefreshRate,
                sysRefreshHz = initSys.RefreshRateHz
            });

            // Tab 3: Hardware & Monitor + Click-Through Overlay HUD Verification
            await mainCore.ExecuteScriptAsync("document.querySelector('.tab-btn[data-nav=\"monitor\"]').click()");
            bool monitorDomOk = await WaitUntilAsync(
                mainCore,
                "document.querySelector('.tab-btn[data-nav=\"monitor\"]')?.classList.contains('active') && document.querySelectorAll('[data-osd-level]').length === 5 && document.querySelectorAll('[data-cpu-boost]').length === 3 && document.querySelectorAll('svg.sparkline-svg').length === 2 && Boolean(document.getElementById('kpiFps')?.textContent?.trim())");
            await Task.Delay(250);
            await PrepareCleanCaptureAsync(mainCore);
            await CapturePngAsync(mainCore, outputDir, "v5_06_tab3_hardware_monitor.png");

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

            // Tab 3: Simplified Controls, Back-Key Picker Modal, TabTip OSK, Spatial Focus & Scroll Preservation
            await mainCore.ExecuteScriptAsync("document.querySelector('.tab-btn[data-nav=\"controls\"]').click()");
            bool controlsReady = await WaitUntilAsync(
                mainCore,
                "document.querySelector('.tab-btn[data-nav=\"controls\"]')?.classList.contains('active') && Boolean(document.getElementById('btnReadGpd') && document.getElementById('btnPickL4') && document.getElementById('btnPickR4') && document.getElementById('btnSaveGpd') && document.getElementById('swInterceptOsk') && document.getElementById('btnTestOsk') && document.getElementById('setAutoInject') && document.getElementById('btnToggleControlsAdv')) && document.querySelectorAll('[data-gpd-preset]').length === 4 && document.querySelectorAll('[data-gpd-l4]').length === 0 && document.querySelectorAll('.keycap-pill').length >= 3 && document.querySelectorAll('.gp-focused').length === 1");
            await Task.Delay(200);
            await mainCore.ExecuteScriptAsync("window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))");
            await Task.Delay(120);
            var focusCountStr = await mainCore.ExecuteScriptAsync("document.querySelectorAll('.gp-focused').length");

            // Test back-key picker modal: verify 3 tabs (single/record/combo), select combo preset gp_lbrb ("LB + RB"), then restore 保留原厂设置 (-1)
            await mainCore.ExecuteScriptAsync("document.getElementById('btnPickL4')?.click()");
            bool pickerOpened = await WaitUntilAsync(
                mainCore,
                "!document.getElementById('modalBackdrop').classList.contains('hidden') && Boolean(document.querySelector('[data-back-modal-tab=\"single\"]')) && Boolean(document.querySelector('[data-back-modal-tab=\"record\"]')) && Boolean(document.querySelector('[data-back-modal-tab=\"combo\"]')) && Boolean(document.querySelector('[data-pick-back-key=\"32783\"]'))");
            await mainCore.ExecuteScriptAsync("document.querySelector('[data-back-modal-tab=\"combo\"]')?.click()");
            bool comboTabReady = await WaitUntilAsync(mainCore, "Boolean(document.querySelector('[data-combo-preset=\"gp_lbrb\"]'))");
            await mainCore.ExecuteScriptAsync("document.querySelector('[data-combo-preset=\"gp_lbrb\"]')?.click()");
            bool pickerClosed = await WaitUntilAsync(mainCore, "document.getElementById('modalBackdrop').classList.contains('hidden')");
            bool comboBadgeOk = await WaitUntilAsync(mainCore, "document.getElementById('l4SlotBadge')?.textContent?.trim() === 'LB + RB'");
            await mainCore.ExecuteScriptAsync("document.getElementById('btnPickL4')?.click()");
            await WaitUntilAsync(mainCore, "!document.getElementById('modalBackdrop').classList.contains('hidden') && Boolean(document.querySelector('[data-pick-back-key=\"-1\"]'))");
            await mainCore.ExecuteScriptAsync("document.querySelector('[data-pick-back-key=\"-1\"]')?.click()");
            await WaitUntilAsync(mainCore, "document.getElementById('modalBackdrop').classList.contains('hidden')");
            bool modalPickerOk = pickerOpened && comboTabReady && pickerClosed && comboBadgeOk;

            // Expand advanced section so .page-scroll has vertical scroll headroom, test scroll preservation across swInterceptOsk toggles, then collapse for clean screenshot
            await mainCore.ExecuteScriptAsync("document.getElementById('btnToggleControlsAdv')?.click()");
            bool advExpanded = await WaitUntilAsync(mainCore, "Boolean(document.getElementById('btnHotkeyQam'))");
            await Task.Delay(150);

            var scrollBeforeJson = await mainCore.ExecuteScriptAsync("""
                (() => {
                  const scroller = document.querySelector('.page-scroll');
                  if (!scroller) return { maxScroll: 0, before: 0 };
                  const maxScroll = Math.max(0, scroller.scrollHeight - scroller.clientHeight);
                  if (maxScroll >= 40) scroller.scrollTop = Math.min(160, maxScroll);
                  return { maxScroll, before: scroller.scrollTop };
                })()
                """);
            using var scrollBeforeDoc = JsonDocument.Parse(scrollBeforeJson);
            double maxControlsScroll = scrollBeforeDoc.RootElement.GetProperty("maxScroll").GetDouble();
            double scrollBefore = scrollBeforeDoc.RootElement.GetProperty("before").GetDouble();
            await mainCore.ExecuteScriptAsync("document.getElementById('swInterceptOsk')?.click()");
            await Task.Delay(180);
            await mainCore.ExecuteScriptAsync("document.getElementById('swInterceptOsk')?.click()");
            await Task.Delay(180);
            var scrollAfterStr = await mainCore.ExecuteScriptAsync("document.querySelector('.page-scroll')?.scrollTop ?? 0");
            double.TryParse(scrollAfterStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var scrollAfter);
            bool scrollPreservedOnClick = maxControlsScroll < 40 || Math.Abs(scrollAfter - scrollBefore) <= 20;

            await mainCore.ExecuteScriptAsync("document.getElementById('btnToggleControlsAdv')?.click()");
            await WaitUntilAsync(mainCore, "!document.getElementById('btnHotkeyQam')");
            await Task.Delay(180);

            Record("controlsAndSpatialFocus", controlsReady && modalPickerOk && advExpanded && focusCountStr == "1" && scrollPreservedOnClick, new
            {
                controlsReady,
                modalPickerOk,
                advExpanded,
                focusCount = focusCountStr,
                scrollPreservedOnClick,
                scrollBefore,
                scrollAfter,
                maxControlsScroll
            });
            await PrepareCleanCaptureAsync(mainCore);
            await CapturePngAsync(mainCore, outputDir, "v4_07_tab4_controls_settings.png");

            // Tab 2: Unified Game Hardware Profile + OptiScaler Suite (Tune, Library, Components)
            await mainCore.ExecuteScriptAsync("document.querySelector('.tab-btn[data-nav=\"opti\"]').click()");
            bool optiTuneReady = await WaitUntilAsync(
                mainCore,
                "document.querySelector('.sub-tab-btn[data-opti-sub=\"tune\"]')?.classList.contains('active') && document.querySelectorAll('.preset-card').length === 4 && document.querySelectorAll('[data-game-perf-cpu]').length === 4");
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
            await PrepareCleanCaptureAsync(mainCore);
            await CapturePngAsync(mainCore, outputDir, "v5_05_tab2_game_opti.png");

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

            // Opti Sub-tab: Components
            await mainCore.ExecuteScriptAsync("document.querySelector('.sub-tab-btn[data-opti-sub=\"components\"]').click()");
            bool compReady = await WaitUntilAsync(
                mainCore,
                "document.querySelector('.sub-tab-btn[data-opti-sub=\"components\"]')?.classList.contains('active') && document.querySelectorAll('[data-install-comp]').length === 4 && document.querySelectorAll('[data-active-slot]').length === 2");
            await Task.Delay(250);
            var activeKernelStr = await mainCore.ExecuteScriptAsync("document.querySelector('[data-active-slot].active')?.dataset.activeSlot || ''");
            Record("optiComponentsSubTab", compReady, new { activeKernelSlot = activeKernelStr.Trim('"') });

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
            await WaitUntilAsync(mainCore, "document.querySelectorAll('[data-tdp-watt]').length >= 3");
            await mainCore.ExecuteScriptAsync("document.getElementById('btnOpenQam').click()");

            var qamDeadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < qamDeadline && !(qamWindow.IsVisible && qamWindow.WebViewInitialized))
            {
                await Task.Delay(100);
            }

            bool qamVisibleAndInit = qamWindow.IsVisible && qamWindow.WebViewInitialized;
            bool qamHandheldReady = false;
            bool qamZeroVertScroll = false;
            bool qamWidthOk = false;
            bool qamOsdOk = false;
            bool qamControlsOk = false;
            bool qamOptiOk = false;
            int qamErrCount = 0;

            if (qamVisibleAndInit && qamWindow.QamWebView.CoreWebView2 is { } qamCore)
            {
                qamHandheldReady = await WaitUntilAsync(
                    qamCore,
                    "document.body.classList.contains('mode-qam') && !document.getElementById('qamRail').classList.contains('hidden') && document.querySelector('.qam-rail-btn[data-qam-tab=\"handheld\"]')?.classList.contains('active') && document.querySelectorAll('.qam-rail-btn').length === 4 && document.querySelectorAll('select').length === 0 && document.documentElement.scrollWidth <= document.documentElement.clientWidth + 2 && document.querySelectorAll('.gp-focused').length === 1 && document.getElementById('btnTdpDec')?.textContent?.trim() === '−1W' && document.getElementById('btnTdpInc')?.textContent?.trim() === '+1W' && Boolean(document.getElementById('brightRange')) && Boolean(document.getElementById('volRange')) && document.querySelectorAll('[data-refresh-hz]').length >= 2 && document.querySelectorAll('[data-osd-level]').length === 5 && document.querySelectorAll('[data-cpu-boost]').length === 3 && document.querySelectorAll('[data-fan-mode]').length === 3 && document.querySelectorAll('.toolbox-tile').length === 8 && Boolean(document.querySelector('[data-toolbox=\"toggle_fridge\"]'))");

                var qamLiveSlideOkStr = await qamCore.ExecuteScriptAsync("""
                    (() => {
                      const r = document.getElementById('tdpRangeInput');
                      const ro = document.getElementById('qamTdpReadout');
                      if (!r || !ro) return false;
                      const orig = r.value;
                      const probe = orig === '47' ? '48' : '47';
                      r.value = probe;
                      r.dispatchEvent(new Event('input', { bubbles: true }));
                      const updated = ro.textContent.includes(probe + 'W');
                      r.value = orig;
                      r.dispatchEvent(new Event('input', { bubbles: true }));
                      return updated;
                    })()
                    """);
                qamHandheldReady = qamHandheldReady && string.Equals(qamLiveSlideOkStr.Trim(), "true", StringComparison.OrdinalIgnoreCase);

                var qamZeroScrollStr = await qamCore.ExecuteScriptAsync("(() => { const mv = document.getElementById('mainView'); return Boolean(mv) && mv.scrollHeight <= mv.clientHeight + 2; })()");
                qamZeroVertScroll = string.Equals(qamZeroScrollStr.Trim(), "true", StringComparison.OrdinalIgnoreCase);

                await Task.Delay(200);
                qamWidthOk = Math.Abs(qamWindow.ActualWidth - 440) < 2;

                await SwitchLayoutAndThemeAsync("bento", "aurora");
                await PrepareCleanCaptureAsync(qamCore);
                await CapturePngAsync(qamCore, outputDir, "v5_08_qam_quick_aurora.png");

                await SwitchLayoutAndThemeAsync("bento", "frost");
                await PrepareCleanCaptureAsync(qamCore);
                await CapturePngAsync(qamCore, outputDir, "v5_09_qam_quick_frost.png");

                await SwitchLayoutAndThemeAsync(origUiLayout, origUiTheme);
                await PrepareCleanCaptureAsync(qamCore);

                await qamCore.ExecuteScriptAsync("document.querySelector('.qam-rail-btn[data-qam-tab=\"opti\"]').click()");
                qamOptiOk = await WaitUntilAsync(
                    qamCore,
                    "document.querySelector('.qam-rail-btn[data-qam-tab=\"opti\"]')?.classList.contains('active') && Boolean(document.getElementById('qamStatusBanner')) && Boolean(document.getElementById('btnQamOpenGamePicker')) && document.querySelectorAll('[data-preset]').length >= 3 && document.documentElement.scrollWidth <= document.documentElement.clientWidth + 2");
                await Task.Delay(180);
                await PrepareCleanCaptureAsync(qamCore);
                await CapturePngAsync(qamCore, outputDir, "v4_10_qam_opti.png");

                await qamCore.ExecuteScriptAsync("document.getElementById('btnQamOpenGamePicker').click()");
                bool qamGamesPickerOk = await WaitUntilAsync(
                    qamCore,
                    "Boolean(document.querySelector('.qam-game-picker-view')) && Boolean(document.getElementById('btnQamBackToTune')) && document.querySelectorAll('.qam-game-row').length >= 1");
                await qamCore.ExecuteScriptAsync("document.getElementById('btnQamBackToTune').click()");
                await WaitUntilAsync(qamCore, "Boolean(document.getElementById('qamStatusBanner'))");
                qamOptiOk = qamOptiOk && qamGamesPickerOk;

                await qamCore.ExecuteScriptAsync("document.querySelector('.qam-rail-btn[data-qam-tab=\"osd\"]').click()");
                qamOsdOk = await WaitUntilAsync(
                    qamCore,
                    "document.querySelector('.qam-rail-btn[data-qam-tab=\"osd\"]')?.classList.contains('active') && Boolean(document.querySelector('#qamOsdSparkline svg')) && Boolean(document.getElementById('qamKpiFps')?.textContent?.trim()) && document.querySelectorAll('[data-cpu-boost]').length === 3 && document.documentElement.scrollWidth <= document.documentElement.clientWidth + 2");
                await Task.Delay(180);
                await PrepareCleanCaptureAsync(qamCore);
                await CapturePngAsync(qamCore, outputDir, "v4_11_qam_osd.png");

                await qamCore.ExecuteScriptAsync("document.querySelector('.qam-rail-btn[data-qam-tab=\"controls\"]').click()");
                qamControlsOk = await WaitUntilAsync(
                    qamCore,
                    "document.querySelector('.qam-rail-btn[data-qam-tab=\"controls\"]')?.classList.contains('active') && Boolean(document.getElementById('swInterceptMenuBtn')) && Boolean(document.getElementById('swInterceptOsk')) && Boolean(document.getElementById('btnPickL4')) && Boolean(document.getElementById('btnPickR4')) && document.querySelectorAll('[data-ui-layout]').length === 2 && document.querySelectorAll('[data-ui-theme]').length === 2 && document.documentElement.scrollWidth <= document.documentElement.clientWidth + 2");
                await Task.Delay(180);
                await PrepareCleanCaptureAsync(qamCore);
                await CapturePngAsync(qamCore, outputDir, "v4_12_qam_controls.png");

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
            Record("qamHandheldFirstAndClose", qamVisibleAndInit && qamHandheldReady && qamZeroVertScroll && qamWidthOk && qamOsdOk && qamControlsOk && qamOptiOk && qamClosed, new
            {
                qamVisibleAndInit,
                qamHandheldReady,
                qamZeroVertScroll,
                qamWidthOk,
                qamOsdOk,
                qamControlsOk,
                qamOptiOk,
                qamActualWidth = Math.Round(qamWindow.ActualWidth, 1),
                qamClosed
            });

            // Fullscreen Overlay Z-Order & Non-Activating Focus Preservation Regression Check
            {
                int prevHudLevel = bridge.Settings.OverlayLevel;
                string prevHudPos = bridge.Settings.OverlayPosition;
                double prevHudOpacity = bridge.Settings.OverlayOpacity;
                Window? dummyGame = null;
                try
                {
                    overlayHudWindow.ApplySettings(2, "top-left", 0.88);
                    overlayHudWindow.UpdateMetrics(bridge.LatestSystemState, bridge.RunningGame, bridge.Settings.CurrentTdpWatts);
                    await Task.Delay(80);

                    dummyGame = new System.Windows.Window
                    {
                        WindowStyle = WindowStyle.None,
                        ResizeMode = ResizeMode.NoResize,
                        Width = 320,
                        Height = 240,
                        Left = 10,
                        Top = 10,
                        Topmost = true,
                        ShowInTaskbar = false,
                        Background = System.Windows.Media.Brushes.Black
                    };
                    dummyGame.Show();
                    dummyGame.Activate();
                    var dummyHwnd = new WindowInteropHelper(dummyGame).Handle;
                    Win32Interop.ForceForegroundOverlay(dummyHwnd);
                    await Task.Delay(80);

                    var testHudHwnd = new WindowInteropHelper(overlayHudWindow).Handle;
                    overlayHudWindow.EnsureTopmostZOrder(forceReband: true);
                    await Task.Delay(50);
                    bool hudBeatTopmostGame = !Win32Interop.HasVisibleWindowAbove(testHudHwnd);

                    nint fgBeforeQam = Win32Interop.GetForegroundWindow();
                    await qamWindow.ShowOverlayAsync();
                    overlayHudWindow.EnsureTopmostZOrder(forceReband: false);
                    await Task.Delay(150);

                    nint qamHwnd = new WindowInteropHelper(qamWindow).Handle;
                    bool qamFocusableStyle = (Win32Interop.GetWindowLong(qamHwnd, Win32Interop.GWL_EXSTYLE) & Win32Interop.WS_EX_NOACTIVATE) == 0;
                    nint fgDuringQam = Win32Interop.GetForegroundWindow();
                    bool qamTookFocusFromGame = qamFocusableStyle &&
                                                fgDuringQam != dummyHwnd &&
                                                (fgDuringQam == 0 || Win32Interop.IsWindowOrChildForeground(qamHwnd));
                    bool qamAboveTopmostGame = !Win32Interop.HasVisibleWindowAbove(qamHwnd, allowedAboveHwnd: testHudHwnd);
                    bool hudStillOnTop = !Win32Interop.HasVisibleWindowAbove(testHudHwnd);

                    bool simulatedHeld = true;
                    var hideShieldTask = qamWindow.HideAndRestoreGameFocusAsync(() => simulatedHeld, forceShield: true);
                    await Task.Delay(60);
                    bool hasParkedRect = Win32Interop.GetWindowRect(qamHwnd, out var parkedRect);
                    bool shieldHeldActive = qamWindow.IsClosingShieldActive &&
                                            qamWindow.IsVisible &&
                                            hasParkedRect &&
                                            parkedRect.Left <= -10000;
                    simulatedHeld = false;
                    var drainSw = Stopwatch.StartNew();
                    await hideShieldTask;
                    drainSw.Stop();
                    bool shieldReleasedCleanly = !qamWindow.IsVisible &&
                                                 !qamWindow.IsClosingShieldActive &&
                                                 qamWindow.Left >= 0 &&
                                                 drainSw.ElapsedMilliseconds >= 130;
                    bool closingShieldHeldAndReleased = shieldHeldActive && shieldReleasedCleanly;
                    await Task.Delay(60);

                    // Verify SW_RESTORE path for minimized exclusive-fullscreen games
                    Win32Interop.ShowWindow(dummyHwnd, 6 /* SW_MINIMIZE */);
                    await Task.Delay(50);
                    bool wasIconic = Win32Interop.IsIconic(dummyHwnd);
                    Win32Interop.RestoreWindowFocusIfValid(dummyHwnd);
                    await Task.Delay(50);
                    bool iconicGameRestored = wasIconic && !Win32Interop.IsIconic(dummyHwnd);

                    Record("fullscreenOverlayCompatibility",
                        hudBeatTopmostGame && qamFocusableStyle && qamTookFocusFromGame && qamAboveTopmostGame && hudStillOnTop && closingShieldHeldAndReleased && iconicGameRestored,
                        new
                        {
                            hudBeatTopmostGame,
                            qamFocusableStyle,
                            qamIsActive = qamWindow.IsActive,
                            qamTookFocusFromGame,
                            qamAboveTopmostGame,
                            hudStillOnTop,
                            shieldHeldActive,
                            parkedLeft = parkedRect.Left,
                            drainElapsedMs = drainSw.ElapsedMilliseconds,
                            shieldReleasedCleanly,
                            closingShieldHeldAndReleased,
                            iconicGameRestored,
                            fgBeforeQam = $"0x{fgBeforeQam:X}",
                            fgDuringQam = $"0x{fgDuringQam:X}",
                            dummyHwnd = $"0x{dummyHwnd:X}"
                        });
                }
                finally
                {
                    if (qamWindow.IsVisible)
                    {
                        qamWindow.Hide();
                    }
                    dummyGame?.Close();
                    overlayHudWindow.ApplySettings(prevHudLevel, prevHudPos, prevHudOpacity);
                }
            }

            // Live OSK open + close toggle verification (both physical Ctrl+Win+O chord race and live WebView2 UI click)
            if (HandheldToolboxService.QueryTabTipVisible())
            {
                await Task.Run(() => HandheldToolboxService.ToggleOnScreenKeyboard("tabtip", fromUiClick: false, forceClose: true));
                await Task.Delay(500);
            }

            // Cycle 1: Simulated physical Ctrl+Win+O race (TabTip open -> Ctrl tap starts auto-dismiss -> 90ms later chord toggles close -> must stay closed)
            string chordOpenMsg = await Task.Run(() => HandheldToolboxService.ToggleOnScreenKeyboard("tabtip", fromUiClick: false, forceClose: false));
            bool chordOpened = false;
            var chordOpenDeadline = DateTime.UtcNow.AddMilliseconds(1500);
            while (DateTime.UtcNow < chordOpenDeadline)
            {
                if (HandheldToolboxService.QueryTabTipVisible())
                {
                    chordOpened = true;
                    break;
                }
                await Task.Delay(50);
            }

            await Task.Delay(400);
            bool hookSawOpenBeforeChord = HandheldToolboxService.ShouldCloseFromHookSnapshot();
            Win32Interop.SendStandaloneCtrlTap();
            await Task.Delay(90);
            string chordCloseMsg = await Task.Run(() => HandheldToolboxService.ToggleOnScreenKeyboard(
                "tabtip",
                fromUiClick: false,
                forceClose: hookSawOpenBeforeChord,
                fromPhysicalChord: true));

            bool chordClosed = false;
            var chordCloseDeadline = DateTime.UtcNow.AddMilliseconds(1200);
            while (DateTime.UtcNow < chordCloseDeadline)
            {
                if (!HandheldToolboxService.QueryTabTipVisible())
                {
                    chordClosed = true;
                    break;
                }
                await Task.Delay(50);
            }

            bool chordStayedClosed = chordClosed;
            if (chordClosed)
            {
                for (int elapsed = 0; elapsed < 650; elapsed += 60)
                {
                    await Task.Delay(60);
                    if (HandheldToolboxService.QueryTabTipVisible())
                    {
                        chordStayedClosed = false;
                        break;
                    }
                }
            }

            // Cycle 2: Live WebView2 #btnTestOsk click open -> click close -> must stay closed
            await mainCore.ExecuteScriptAsync("document.querySelector('.tab-btn[data-nav=\"controls\"]')?.click()");
            bool controlsReadyForOsk = await WaitUntilAsync(mainCore, "Boolean(document.getElementById('btnTestOsk'))");
            await Task.Delay(150);

            await mainCore.ExecuteScriptAsync("document.getElementById('btnTestOsk')?.click()");
            bool uiClickOpened = false;
            var uiOpenDeadline = DateTime.UtcNow.AddMilliseconds(1500);
            while (DateTime.UtcNow < uiOpenDeadline)
            {
                if (HandheldToolboxService.QueryTabTipVisible())
                {
                    uiClickOpened = true;
                    break;
                }
                await Task.Delay(50);
            }

            await Task.Delay(520);
            await mainCore.ExecuteScriptAsync("document.getElementById('btnTestOsk')?.click()");
            bool uiClickClosed = false;
            var uiCloseDeadline = DateTime.UtcNow.AddMilliseconds(1500);
            while (DateTime.UtcNow < uiCloseDeadline)
            {
                if (!HandheldToolboxService.QueryTabTipVisible())
                {
                    uiClickClosed = true;
                    break;
                }
                await Task.Delay(50);
            }

            bool uiClickStayedClosed = uiClickClosed;
            if (uiClickClosed)
            {
                for (int elapsed = 0; elapsed < 650; elapsed += 60)
                {
                    await Task.Delay(60);
                    if (HandheldToolboxService.QueryTabTipVisible())
                    {
                        uiClickStayedClosed = false;
                        break;
                    }
                }
            }

            bool noGpdKeyboardSpawned = Process.GetProcessesByName("GPDKeyboard").Length == 0;
            Record("oskTabTipOpenAndClose",
                chordOpened && hookSawOpenBeforeChord && chordStayedClosed &&
                controlsReadyForOsk && uiClickOpened && uiClickStayedClosed && noGpdKeyboardSpawned,
                new
                {
                    chordOpened,
                    hookSawOpenBeforeChord,
                    chordClosed,
                    chordStayedClosed,
                    controlsReadyForOsk,
                    uiClickOpened,
                    uiClickClosed,
                    uiClickStayedClosed,
                    noGpdKeyboardSpawned,
                    chordOpenMsg,
                    chordCloseMsg
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
