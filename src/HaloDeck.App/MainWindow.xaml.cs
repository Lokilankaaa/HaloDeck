using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using HaloDeck.App.Bridge;
using HaloDeck.App.Native;

namespace HaloDeck.App;

public partial class MainWindow : Window
{
    private readonly WebUiBridge _bridge;
    private Task? _webViewInitTask;
    private bool _allowClose;
    private bool _isFullscreen;
    private WindowStyle _savedStyle = WindowStyle.SingleBorderWindow;
    private WindowState _savedState = WindowState.Normal;
    private ResizeMode _savedResize = ResizeMode.CanResize;

    public bool WebViewInitialized { get; private set; }

    public MainWindow(WebUiBridge bridge)
    {
        _bridge = bridge;
        InitializeComponent();

        var workArea = SystemParameters.WorkArea;
        if (Width > workArea.Width - 24)
        {
            Width = Math.Max(MinWidth, workArea.Width - 24);
        }
        if (Height > workArea.Height - 20)
        {
            Height = Math.Max(MinHeight, workArea.Height - 20);
        }

        Loaded += async (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32Interop.EnableRoundedCorners(hwnd);
            await EnsureInitializedOffscreenForVerifyAsync();
        };
    }

    public Task EnsureInitializedOffscreenForVerifyAsync()
    {
        return _webViewInitTask ??= InitializeWebViewCoreAsync();
    }

    private async Task InitializeWebViewCoreAsync()
    {
        await _bridge.AttachWebViewAsync(ConsoleWebView, "console");
        WebViewInitialized = true;
    }

    public void SetFullscreenMode(bool fullscreen)
    {
        if (_isFullscreen == fullscreen) return;
        _isFullscreen = fullscreen;

        if (fullscreen)
        {
            _savedStyle = WindowStyle;
            _savedState = WindowState;
            _savedResize = ResizeMode;

            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = _savedStyle;
            ResizeMode = _savedResize;
            WindowState = _savedState;
        }
    }

    public void ToggleFullscreen() => SetFullscreenMode(!_isFullscreen);

    public void AllowRealClose() => _allowClose = true;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }
}
