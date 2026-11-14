using System.Windows;
using System.Windows.Interop;
using HandheldOpti.App.Bridge;
using HandheldOpti.App.Native;

namespace HandheldOpti.App;

public partial class QamWindow : Window
{
    private readonly WebUiBridge _bridge;
    private bool _initialized;
    private nint _previousForegroundHwnd;

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

        PositionOnRightEdge();
        Show();
        Activate();

        if (!_initialized)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32Interop.EnableRoundedCorners(hwnd);
            await _bridge.AttachWebViewAsync(QamWebView, "qam");
            _initialized = true;
        }
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
