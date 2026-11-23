using System.Text.RegularExpressions;
using HaloDeck.Core.Config;
using HaloDeck.Core.Models;
using HaloDeck.Core.Services;
using Xunit;

namespace HaloDeck.Tests;

public sealed class HaloDeckV054RtssOsdTests
{
    private static RtssOsdMetrics SampleMetrics => new(
        CurrentFps: 118,
        FrameTimeMs: 8.5,
        ApuPowerWatts: 42,
        TargetTdpWatts: 55,
        GpuUsagePercent: 96,
        GfxClockMhz: 2150,
        GpuTempC: 68,
        CpuUsagePercent: 31,
        CpuClockMhz: 3800,
        CpuTempC: 64,
        VramUsedMb: 8600,
        VramTotalMb: 16384,
        UsedRamGb: 18.4,
        FanRpm: 3120,
        HasBattery: true,
        BatteryPercent: 82,
        SystemDrawWatts: 56.2,
        PowerRuntimeMode: "battery",
        BatteryDischargeWatts: 56.2,
        BatteryPowerWatts: null,
        EstimatedStandbyMinutes: 95);

    [Fact]
    public void ResolveEffectiveBackend_HandlesAllThreeModesAndFallbacks()
    {
        Assert.Equal("off", RtssOsdFormatter.ResolveEffectiveBackend("rtss", 0, rtssRunning: true, isExclusiveFullscreen: true));
        Assert.Equal("native", RtssOsdFormatter.ResolveEffectiveBackend("native", 3, rtssRunning: true, isExclusiveFullscreen: true));
        Assert.Equal("native", RtssOsdFormatter.ResolveEffectiveBackend("auto_rtss", 3, rtssRunning: true, isExclusiveFullscreen: false));
        Assert.Equal("rtss", RtssOsdFormatter.ResolveEffectiveBackend("auto_rtss", 3, rtssRunning: true, isExclusiveFullscreen: true));
        Assert.Equal("native", RtssOsdFormatter.ResolveEffectiveBackend("auto_rtss", 3, rtssRunning: false, isExclusiveFullscreen: true));
        Assert.Equal("rtss", RtssOsdFormatter.ResolveEffectiveBackend("rtss", 2, rtssRunning: true, isExclusiveFullscreen: false));
        Assert.Equal("native", RtssOsdFormatter.ResolveEffectiveBackend("rtss", 2, rtssRunning: false, isExclusiveFullscreen: true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void BuildFormattedText_EmitsUniversalRtss72And73SingleLayerCardWithoutUnsupportedTags(int level)
    {
        var payload = RtssOsdFormatter.BuildPayload(level, SampleMetrics);
        string text = payload.Text;
        Assert.False(string.IsNullOrWhiteSpace(text));
        // Strictly compatible with RTSS 7.3.2..7.3.7: never emit 7.3.5+-only tags that print literally on older RTSS
        Assert.DoesNotContain("<FNT=", text);
        Assert.DoesNotContain("<M=", text);
        Assert.DoesNotContain("<B=0,0,1>", text);
        Assert.DoesNotContain("<B=0,0,R8>", text);
        Assert.DoesNotContain("<L1>", text);
        Assert.Contains("<L0><C0><B=0,0>\b<C><E=-", text);
        Assert.Contains("<FR>", text);
        Assert.Contains("APU", text);
        Assert.All(text, ch => Assert.True(ch <= 0x7F, $"Unexpected non-ASCII char U+{(int)ch:X4} for RTSS hypertext"));

        // Ensure computed extent width strictly exceeds visible character length so RTSS never wraps/clips
        var match = Regex.Match(text, @"<E=-(\d+),-\d+(?:,\d+)?>");
        Assert.True(match.Success);
        int extentCols = int.Parse(match.Groups[1].Value);
        string body = text[(match.Index + match.Length)..];
        int visibleCols = RtssOsdFormatter.MeasureMaxVisibleLineChars(body);
        Assert.True(extentCols >= visibleCols + 2, $"Level {level}: extentCols={extentCols} must be >= visibleCols+2 ({visibleCols + 2})");

        if (level == 4)
        {
            Assert.Contains("<OBJ=00000000>", text);
            Assert.NotNull(payload.EmbeddedObjectBuffer);
            Assert.Equal(36, payload.EmbeddedObjectBuffer!.Length);
            Assert.Equal(0x47523030u, BitConverter.ToUInt32(payload.EmbeddedObjectBuffer, 0));
            Assert.Equal(50000.0f, BitConverter.ToSingle(payload.EmbeddedObjectBuffer, 28));
        }
        else
        {
            Assert.Null(payload.EmbeddedObjectBuffer);
        }
    }

    [Theory]
    [InlineData("top-left", "<P0>")]
    [InlineData("top-center", "<P1>")]
    [InlineData("top-right", "<P2>")]
    public void BuildPayload_MapsPositionToRtssStickyAnchor(string position, string expectedTag)
    {
        var payload = RtssOsdFormatter.BuildPayload(2, SampleMetrics with { Position = position });
        Assert.Contains(expectedTag + "<L0><C0><B=0,0>\b<C>", payload.Text);
    }

    [Fact]
    public void SettingsStore_NormalizesOverlayRendererModes()
    {
        var s = new AppSettings { OverlayRenderer = "AUTO_RTSS" };
        SettingsStore.Normalize(s);
        Assert.Equal("auto_rtss", s.OverlayRenderer);

        s.OverlayRenderer = "rtss";
        SettingsStore.Normalize(s);
        Assert.Equal("rtss", s.OverlayRenderer);

        s.OverlayRenderer = "invalid_mode";
        SettingsStore.Normalize(s);
        Assert.Equal("native", s.OverlayRenderer);
    }
}
