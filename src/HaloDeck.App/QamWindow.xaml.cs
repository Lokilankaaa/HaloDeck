using System.Windows;
using System.Windows.Interop;
using HaloDeck.App.Bridge;
using HaloDeck.App.Native;
using HaloDeck.Core.Services;

namespace HaloDeck.App;

public partial class QamWindow : Window
{
    private readonly WebUiBridge _bridge;
    private Task? _webViewInitTask;
    private nint _previousForegroundHwnd;
    private DateTime _shownAtUtc = DateTime.MinValue;
    private bool _isClosingShieldActive;
    private int _transitionVersion;

    public bool WebViewInitialized { get; private set; }
    public nint PreviousForegroundHwnd => _previousForegroundHwnd;
    public DateTime ShownAtUtc => _shownAtUtc;
    public bool IsClosingShieldActive => _isClosingShieldActive;
    public bool IsInteractiveVisible => IsVisible && !_isClosingShieldActive;
    public event Action? ClosingShieldChanged;

    public QamWindow(WebUiBridge bridge)
    {
        _bridge = bridge;
        InitializeComponent();
        PositionOnRightEdge();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(PositionOnRightEdge);
    }

    protected override void OnClosed(EventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32Interop.ApplyFocusableTopmostOverlayStyle(hwnd);
        Win32Interop.EnableRoundedCorners(hwnd);
    }

    public void EnsureTopmostAboveGame(nint hudHwnd, bool forceReband = false)
    {
        if (!IsInteractiveVisible) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;

        Win32Interop.ApplyFocusableTopmostOverlayStyle(hwnd);
        if (forceReband || Win32Interop.HasVisibleWindowAbove(hwnd, allowedAboveHwnd: hudHwnd))
        {
            Win32Interop.RaiseTopmostNoActivate(hwnd, forceReband, allowedAboveHwnd: hudHwnd);
        }

        if (FrontendFocusArbitrator.ShouldReassertQamForeground(
                IsInteractiveVisible,
                (DateTime.UtcNow - _shownAtUtc).TotalMilliseconds,
                Win32Interop.GetForegroundWindow(),
                Win32Interop.IsWindowOrChildForeground(hwnd)))
        {
            Win32Interop.ForceForegroundOverlay(hwnd);
            QamWebView.Focus();
        }
    }

    public async Task ToggleOverlayAsync()
    {
        if (IsInteractiveVisible)
        {
            await HideAndRestoreGameFocusAsync();
            return;
        }

        await ShowOverlayAsync();
    }

    public async Task ShowOverlayAsync()
    {
        int ver = ++_transitionVersion;
        _isClosingShieldActive = false;
        Opacity = 1.0;
        IsHitTestVisible = true;

        var fg = Win32Interop.GetForegroundWindow();
        var selfMain = System.Windows.Application.Current.MainWindow is Window mw
            ? new WindowInteropHelper(mw).Handle
            : 0;
        var selfQam = new WindowInteropHelper(this).Handle;

        if (fg != 0 && fg != selfMain && fg != selfQam)
        {
            Win32Interop.GetWindowThreadProcessId(fg, out uint fgPid);
            if (fgPid != (uint)Environment.ProcessId)
            {
                _previousForegroundHwnd = fg;
            }
        }

        _shownAtUtc = DateTime.UtcNow;
        _bridge.RefreshActiveGameForOverlay();
        _bridge.MuteActiveGameControllerInput(_previousForegroundHwnd);

        PositionOnRightEdge();
        Topmost = true;
        Show();
        Activate();

        var qamHwnd = new WindowInteropHelper(this).Handle;
        Win32Interop.ApplyFocusableTopmostOverlayStyle(qamHwnd);
        Win32Interop.ForceForegroundOverlay(qamHwnd);

        await (_webViewInitTask ??= InitializeWebViewCoreAsync());
        if (!IsVisible || ver != _transitionVersion) return;

        _shownAtUtc = DateTime.UtcNow;
        Activate();
        Win32Interop.ForceForegroundOverlay(qamHwnd);
        QamWebView.Focus();
        ClosingShieldChanged?.Invoke();
    }

    private async Task InitializeWebViewCoreAsync()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32Interop.EnableRoundedCorners(hwnd);
        await _bridge.AttachWebViewAsync(QamWebView, "qam");
        WebViewInitialized = true;
    }

    public void HideWithoutRestoringGame(bool restoreController = true)
    {
        ++_transitionVersion;
        _isClosingShieldActive = false;
        Opacity = 1.0;
        IsHitTestVisible = true;
        if (IsVisible)
        {
            Hide();
        }
        PositionOnRightEdge();
        if (restoreController)
        {
            _bridge.RestoreMutedGameControllerInput();
        }
        ClosingShieldChanged?.Invoke();
    }

    public void HideAndRestoreGameFocus()
    {
        _ = HideAndRestoreGameFocusAsync();
    }

    public async Task HideAndRestoreGameFocusAsync(
        Func<bool>? isButtonHeldOverride = null,
        bool forceShield = false)
    {
        if (!IsVisible || _isClosingShieldActive) return;

        int ver = ++_transitionVersion;
        var holdCheck = isButtonHeldOverride ?? (() =>
            XInputInterop.HasAnyActiveButtonOrTrigger() ||
            (Win32Interop.GetAsyncKeyState(0x1B /* VK_ESCAPE */) & 0x8000) != 0);

        bool shouldShield = forceShield || FrontendFocusArbitrator.ShouldEngageCloseFocusShield(
            holdCheck(),
            XInputInterop.MillisecondsSinceLastButtonOrTrigger(),
            (Win32Interop.GetAsyncKeyState(0x1B) & 0x8000) != 0);

        if (shouldShield)
        {
            _isClosingShieldActive = true;
            IsHitTestVisible = false;

            // Instantly park native HWND + WebView2 child HWND offscreen in 0ms while keeping WS_VISIBLE
            // and retaining Win32 foreground focus on QamWindow so the game sees neither press nor release.
            var qamHwnd = new WindowInteropHelper(this).Handle;
            Left = -32000;
            Top = -32000;
            Win32Interop.ParkWindowOffscreenNoActivate(qamHwnd);
            ClosingShieldChanged?.Invoke();

            var maxDeadline = DateTime.UtcNow.AddMilliseconds(FrontendFocusArbitrator.MaxCloseReleaseWaitMs);
            int cleanMs = 0;
            while (DateTime.UtcNow < maxDeadline && ver == _transitionVersion)
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

            if (ver != _transitionVersion) return;
        }

        _isClosingShieldActive = false;
        Opacity = 1.0;
        IsHitTestVisible = true;
        Hide();
        PositionOnRightEdge();
        _bridge.RestoreMutedGameControllerInput();

        if (_previousForegroundHwnd != 0 && Win32Interop.IsWindow(_previousForegroundHwnd))
        {
            Win32Interop.RestoreWindowFocusIfValid(_previousForegroundHwnd);
        }
        ClosingShieldChanged?.Invoke();
    }

    public void RepositionOnRightEdge() => PositionOnRightEdge();

    private void PositionOnRightEdge()
    {
        const double panelWidth = 440;
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        double targetLeft = Math.Max(0, screenWidth - panelWidth);

        Width = panelWidth;
        Height = screenHeight;
        Left = targetLeft;
        Top = 0;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0 && PresentationSource.FromVisual(this)?.CompositionTarget is { } ct)
        {
            int pxX = (int)Math.Round(targetLeft * ct.TransformToDevice.M11);
            int pxW = (int)Math.Round(panelWidth * ct.TransformToDevice.M11);
            int pxH = (int)Math.Round(screenHeight * ct.TransformToDevice.M22);
            Win32Interop.SetWindowPos(
                hwnd,
                Win32Interop.HWND_TOPMOST,
                pxX,
                0,
                pxW,
                pxH,
                Win32Interop.SWP_NOACTIVATE | Win32Interop.SWP_NOOWNERZORDER);
        }
    }
}
