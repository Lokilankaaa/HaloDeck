using System.Windows;
using System.Windows.Interop;
using HaloDeck.App.Bridge;
using HaloDeck.App.Native;

namespace HaloDeck.App;

public partial class QamWindow : Window
{
    private readonly WebUiBridge _bridge;
    private Task? _webViewInitTask;
    private nint _previousForegroundHwnd;

    public bool WebViewInitialized { get; private set; }

    public QamWindow(WebUiBridge bridge)
    {
        _bridge = bridge;
        InitializeComponent();
        PositionOnRightEdge();
    }

    public async Task ToggleOverlayAsync()
    {
        if (IsVisible)
        {
            HideAndRestoreGameFocus();
            return;
        }

        await ShowOverlayAsync();
    }

    public async Task ShowOverlayAsync()
    {
        var fg = Win32Interop.GetForegroundWindow();
        var selfMain = System.Windows.Application.Current.MainWindow is Window mw
            ? new WindowInteropHelper(mw).Handle
            : 0;
        var selfQam = new WindowInteropHelper(this).Handle;

        if (fg != 0 && fg != selfMain && fg != selfQam)
        {
            _previousForegroundHwnd = fg;
        }

        _bridge.RefreshActiveGameForOverlay();

        PositionOnRightEdge();
        Topmost = true;
        Show();
        Activate();
        Win32Interop.ForceForegroundOverlay(new WindowInteropHelper(this).Handle);

        await (_webViewInitTask ??= InitializeWebViewCoreAsync());
        if (!IsVisible) return;

        Win32Interop.ForceForegroundOverlay(new WindowInteropHelper(this).Handle);
        QamWebView.Focus();
    }

    private async Task InitializeWebViewCoreAsync()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32Interop.EnableRoundedCorners(hwnd);
        await _bridge.AttachWebViewAsync(QamWebView, "qam");
        WebViewInitialized = true;
    }

    public void HideAndRestoreGameFocus()
    {
        if (!IsVisible) return;
        Hide();

        if (_previousForegroundHwnd != 0 && Win32Interop.IsWindow(_previousForegroundHwnd))
        {
            Win32Interop.SetForegroundWindow(_previousForegroundHwnd);
        }
    }

    public void RepositionOnRightEdge() => PositionOnRightEdge();

    private void PositionOnRightEdge()
    {
        const double panelWidth = 440;
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        Width = panelWidth;
        Height = screenHeight;
        Left = Math.Max(0, screenWidth - panelWidth);
        Top = 0;
    }
}
