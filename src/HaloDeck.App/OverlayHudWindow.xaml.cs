using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using HaloDeck.App.Native;
using HaloDeck.Core.Models;

namespace HaloDeck.App;

/// <summary>
/// Steam-Deck style 5-level (0..4) native WPF transparent click-through HUD overlay.
/// Never hooks DirectX pipelines (zero anti-cheat risk) and never steals game focus.
/// </summary>
public partial class OverlayHudWindow : Window
{
    private int _level;
    private string _position = "top-center";
    private double _opacity = 0.86;

    public OverlayHudWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => RepositionWindow();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(RepositionWindow);
    }

    protected override void OnClosed(EventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyClickThroughStyles();
    }

    public void ApplyClickThroughStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        int exStyle = Win32Interop.GetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE);
        exStyle |= Win32Interop.WS_EX_TOPMOST |
                   Win32Interop.WS_EX_TRANSPARENT |
                   Win32Interop.WS_EX_LAYERED |
                   Win32Interop.WS_EX_TOOLWINDOW |
                   Win32Interop.WS_EX_NOACTIVATE;
        Win32Interop.SetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE, exStyle);
    }

    public void EnsureTopmostZOrder(bool forceReband = false)
    {
        if (_level <= 0 || !IsVisible) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;

        ApplyClickThroughStyles();
        if (forceReband || Win32Interop.HasVisibleWindowAbove(hwnd))
        {
            Win32Interop.RaiseTopmostNoActivate(hwnd, forceReband);
        }
    }

    public void ApplySettings(int level, string position, double opacity)
    {
        _level = Math.Clamp(level, 0, 4);
        _position = string.IsNullOrWhiteSpace(position) ? "top-center" : position;
        _opacity = Math.Clamp(opacity, 0.35, 1.0);
        RootCard.Opacity = _opacity;

        if (_level <= 0)
        {
            if (IsVisible) Hide();
            return;
        }

        bool newlyShown = !IsVisible;
        if (newlyShown)
        {
            Show();
            ApplyClickThroughStyles();
        }
        RepositionWindow();
        EnsureTopmostZOrder(forceReband: newlyShown);
    }

    public void UpdateMetrics(SystemControlState sys, GameEntry? runningGame, int targetTdpWatts)
    {
        if (_level <= 0)
        {
            if (IsVisible) Hide();
            return;
        }

        bool newlyShown = !IsVisible;
        if (newlyShown)
        {
            Show();
            ApplyClickThroughStyles();
        }

        string etaShort = FormatMinutesShort(sys.EstimatedStandbyMinutes);
        string batStr = !sys.HasBattery
            ? "AC 直连"
            : sys.PowerRuntimeMode switch
            {
                "charging" => $"整机 {sys.SystemDrawWatts:F1}W · 正在充电 {sys.BatteryPercent}% (+{sys.BatteryPowerWatts ?? 0:F1}W)",
                "ac_sustained" => $"整机 {sys.SystemDrawWatts:F1}W · 插电持续供电 ({sys.BatteryPercent}%)",
                "ac_discharging" => $"整机 {sys.SystemDrawWatts:F1}W · 供电不足 补电-{sys.BatteryDischargeWatts:F1}W{(etaShort != "" ? $" ({etaShort})" : "")}",
                _ => $"{sys.BatteryPercent}% (-{sys.SystemDrawWatts:F1}W{(etaShort != "" ? $" · {etaShort}" : "")})"
            };

        if (_level == 1)
        {
            CompactLineText.Visibility = Visibility.Visible;
            RichPanel.Visibility = Visibility.Collapsed;
            SparklinePanel.Visibility = Visibility.Collapsed;
            RootCard.Padding = new Thickness(10, 5, 10, 5);
            string gameTag = runningGame is { IsCurrentlyRunning: true } ? $"{runningGame.Title} · " : "";
            string fpsPart = sys.CurrentFps > 0
                ? $"{sys.CurrentFps} FPS · "
                : (runningGame is { IsCurrentlyRunning: true } ? "-- FPS · " : "");
            CompactLineText.Text = $"{gameTag}{fpsPart}APU {sys.ApuPowerWatts}W · GPU {sys.GpuTempC}°/CPU {sys.CpuTempC}° · {batStr}";
        }
        else if (_level == 2)
        {
            CompactLineText.Visibility = Visibility.Visible;
            RichPanel.Visibility = Visibility.Collapsed;
            SparklinePanel.Visibility = Visibility.Collapsed;
            RootCard.Padding = new Thickness(12, 6, 12, 6);
            string fpsPart = sys.CurrentFps > 0 ? $"{sys.CurrentFps} FPS | " : "";
            string fanPart = sys.HardwareService.EcReady && sys.HardwareService.FanRpm > 0
                ? $" | FAN {sys.HardwareService.FanRpm}RPM"
                : "";
            double vramGb = sys.VramUsedMb / 1024.0;
            CompactLineText.Text =
                $"{fpsPart}APU {sys.ApuPowerWatts}W/{targetTdpWatts}W | GPU {sys.GpuUsagePercent}% {sys.GpuTempC}°C VRAM {vramGb:F1}G | CPU {sys.CpuUsagePercent}% {sys.CpuTempC}°C{fanPart} | {batStr}";
        }
        else
        {
            CompactLineText.Visibility = Visibility.Collapsed;
            RichPanel.Visibility = Visibility.Visible;
            SparklinePanel.Visibility = _level >= 4 ? Visibility.Visible : Visibility.Collapsed;
            RootCard.Padding = new Thickness(12, 8, 12, 8);

            string title = runningGame is { IsCurrentlyRunning: true }
                ? $"{runningGame.Title}"
                : "HaloDeck 性能监控";
            string injShort = runningGame?.Injection?.IsInjected == true ? " · OptiScaler 已注入" : "";
            string hzLabel = sys.IsDynamicRefreshRate
                ? $"{(sys.BaseRefreshRateHz > 0 ? sys.BaseRefreshRateHz : 60)}-{sys.RefreshRateHz}Hz DRR"
                : $"{sys.RefreshRateHz}Hz";
            HeaderTitleText.Text = title + injShort;
            HeaderRightText.Text = $"{sys.ScreenWidth}×{sys.ScreenHeight} @ {hzLabel}";
            FpsLineText.Text = sys.CurrentFps > 0
                ? $"帧率: {sys.CurrentFps} FPS ({sys.FrameTimeMs:F1} ms)"
                : (sys.HardwareService.DaemonVersion >= 3 ? "帧率: 待检测 DX/全屏渲染" : "帧率: 升级硬件服务 v3 解锁");

            string smuBadge = sys.HardwareService.SmuReady ? $" (设定 {targetTdpWatts}W)" : "";
            ApuLineText.Text = $"APU: {sys.ApuPowerWatts} W{smuBadge}";
            GpuLineText.Text = $"GPU: {sys.GpuUsagePercent}% · {sys.GfxClockMhz} MHz · {sys.GpuTempC}°C";
            CpuLineText.Text = $"CPU: {sys.CpuUsagePercent}% · {sys.CpuClockMhz} MHz · {sys.CpuTempC}°C";

            double vramUsedGb = sys.VramUsedMb / 1024.0;
            double vramTotalGb = sys.VramTotalMb > 0 ? sys.VramTotalMb / 1024.0 : 16.0;
            double sharedGb = sys.SharedVramUsedMb / 1024.0;
            string vramPart = sys.VramUsedMb > 0 || sys.VramTotalMb > 0
                ? $"VRAM {vramUsedGb:F1}/{vramTotalGb:F0}G (共享{sharedGb:F1}G)"
                : $"RAM {sys.UsedRamGb:F1}/{sys.TotalRamGb:F0}G";
            RamLineText.Text = sys.VramUsedMb > 0 || sys.VramTotalMb > 0
                ? $"{vramPart} · RAM {sys.UsedRamGb:F1}G"
                : vramPart;
            FanLineText.Text = sys.HardwareService.ServiceRunning && sys.HardwareService.EcReady
                ? $"风扇: {sys.HardwareService.FanRpm} RPM ({sys.HardwareService.FanMode})"
                : $"调度: {sys.CpuBoostMode}";
            string bypassOrWatts = sys.IsBypassPower
                ? $"整机 {sys.SystemDrawWatts:F1}W · 旁路直供"
                : $"整机 {sys.SystemDrawWatts:F1}W / 输入≈{sys.EstimatedChargerWatts ?? 0:F0}W";
            PowerLineText.Text = sys.PowerRuntimeMode switch
            {
                "charging" => $"{bypassOrWatts} · 正在充电 {sys.BatteryPercent}% (+{sys.BatteryPowerWatts ?? 0:F1}W)",
                "ac_sustained" => $"{bypassOrWatts} · 持续供电 ({sys.BatteryPercent}%)",
                "ac_discharging" => $"整机 {sys.SystemDrawWatts:F1}W · 电池放电-{sys.BatteryDischargeWatts:F1}W · 续航约 {etaShort} ({sys.BatteryPercent}%)",
                _ => !sys.HasBattery
                    ? $"整机: {sys.SystemDrawWatts:F1}W (AC 直连)"
                    : $"整机: {sys.SystemDrawWatts:F1}W · 续航约 {etaShort} ({sys.BatteryPercent}%)"
            };

            if (_level >= 4)
            {
                ApuWattsLine.Points = BuildPolylinePoints(sys.ApuWattsHistory, 280, 34, 85);
                GpuUsageLine.Points = BuildPolylinePoints(sys.GpuUsageHistory, 280, 34, 100);
            }
        }

        UpdateLayout();
        RepositionWindow();
        EnsureTopmostZOrder(forceReband: newlyShown);
    }

    private static string FormatMinutesShort(int? minutes)
    {
        if (minutes is not > 0) return "";
        int h = minutes.Value / 60;
        int m = minutes.Value % 60;
        return h > 0 ? $"{h}h{m:D2}m" : $"{m}m";
    }

    private static PointCollection BuildPolylinePoints(int[] samples, double width, double height, double maxY)
    {
        var pts = new PointCollection();
        if (samples is null || samples.Length == 0) return pts;

        if (samples.Length == 1)
        {
            double y = height - Math.Clamp(samples[0] / maxY, 0, 1) * (height - 4) - 2;
            pts.Add(new System.Windows.Point(0, y));
            pts.Add(new System.Windows.Point(width, y));
            return pts;
        }

        double stepX = width / Math.Max(1, samples.Length - 1);
        for (int i = 0; i < samples.Length; i++)
        {
            double x = i * stepX;
            double y = height - Math.Clamp(samples[i] / maxY, 0, 1) * (height - 4) - 2;
            pts.Add(new System.Windows.Point(x, y));
        }
        return pts;
    }

    private void RepositionWindow()
    {
        double screenW = SystemParameters.PrimaryScreenWidth;
        double w = ActualWidth > 0 ? ActualWidth : 280;
        const double margin = 16;

        Top = margin;
        Left = _position switch
        {
            "top-left" => margin,
            "top-right" => Math.Max(margin, screenW - w - margin),
            _ => Math.Max(margin, (screenW - w) / 2.0)
        };
    }
}
