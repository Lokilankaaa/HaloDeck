using System.Globalization;
using System.Text;

namespace HaloDeck.Core.Services;

public sealed record RtssOsdMetrics(
    int CurrentFps,
    double FrameTimeMs,
    int ApuPowerWatts,
    int TargetTdpWatts,
    int GpuUsagePercent,
    int GfxClockMhz,
    int GpuTempC,
    int CpuUsagePercent,
    int CpuClockMhz,
    int CpuTempC,
    int VramUsedMb,
    int VramTotalMb,
    double UsedRamGb,
    int FanRpm,
    bool HasBattery,
    int BatteryPercent,
    double SystemDrawWatts,
    string PowerRuntimeMode,
    double BatteryDischargeWatts,
    double? BatteryPowerWatts,
    int? EstimatedStandbyMinutes,
    string Position = "top-center",
    double Opacity = 0.86);

public sealed record RtssOsdPayload(string Text, byte[]? EmbeddedObjectBuffer);

/// <summary>
/// Pure routing and universal RTSS 7.2+ / 7.3.x hypertext formatting engine for RTSSSharedMemoryV2.
/// Strictly emits only universally supported tags (&lt;C0..C8&gt;, &lt;P0..P2&gt;, &lt;L0&gt;, &lt;B=0,0&gt;\b, &lt;E&gt;, &lt;FR&gt;, &lt;FT&gt;, &lt;OBJ&gt;)
/// — intentionally avoiding RTSS 7.3.5+-only tags (&lt;FNT&gt;, &lt;M&gt;, 3-arg &lt;B=0,0,1&gt;) that print as raw text on RTSS 7.3.2–7.3.4.
/// </summary>
public static class RtssOsdFormatter
{
    public const string ModeNative = "native";
    public const string ModeAutoRtss = "auto_rtss";
    public const string ModeRtss = "rtss";

    private const uint GraphSignatureGr00 = 0x47523030; // 'GR00'
    private const uint GraphFlagFilled = 1;
    private const uint GraphFlagFrametime = 4;
    private const uint GraphFlagBgnd = 16;
    private const uint GraphFlagAutoscale = 128;

    public static string NormalizeRendererMode(string? mode)
    {
        return (mode ?? ModeNative).Trim().ToLowerInvariant() switch
        {
            ModeAutoRtss => ModeAutoRtss,
            ModeRtss => ModeRtss,
            _ => ModeNative
        };
    }

    public static string ResolveEffectiveBackend(
        string? configuredMode,
        int overlayLevel,
        bool rtssRunning,
        bool isExclusiveFullscreen)
    {
        if (overlayLevel <= 0)
        {
            return "off";
        }

        string mode = NormalizeRendererMode(configuredMode);
        return mode switch
        {
            ModeRtss => rtssRunning ? ModeRtss : ModeNative,
            ModeAutoRtss => (rtssRunning && isExclusiveFullscreen) ? ModeRtss : ModeNative,
            _ => ModeNative
        };
    }

    public static string BuildFormattedText(int level, RtssOsdMetrics m)
        => BuildPayload(level, m).Text;

    public static RtssOsdPayload BuildPayload(int level, RtssOsdMetrics m)
    {
        int clampedLevel = Math.Clamp(level, 0, 4);
        if (clampedLevel <= 0)
        {
            return new RtssOsdPayload(string.Empty, null);
        }

        var ic = CultureInfo.InvariantCulture;
        byte bgAlpha = (byte)Math.Clamp((int)Math.Round(Math.Clamp(m.Opacity, 0.35, 1.0) * 242.0), 125, 240);
        int stickyAnchor = ResolveStickyAnchor(m.Position);

        // Universal RTSS color table:
        // C0 = dark translucent glass pill bg (ARGB), C2..C8 = 100% opaque high-contrast RGB text colors.
        string header = string.Concat(
            $"<C0={bgAlpha:X2}0F172A>",
            "<C2=38BDF8>",
            "<C3=F8FAFC>",
            "<C4=93C5FD>",
            "<C5=86EFAC>",
            "<C6=FCD34D>",
            "<C7=94A3B8>",
            "<C8=64748B>");

        string powerTag = BuildShortPowerTag(m, ic);

        if (clampedLevel == 1)
        {
            // Sleek single-line Steam Deck pill (1 row tall, auto-fitted width)
            string body = string.Concat(
                " <C2><FR> <C7>FPS",
                " <C8>| ",
                $"<C7>APU <C3>{m.ApuPowerWatts}<C7>W",
                " <C8>| ",
                $"<C4>G <C3>{m.GpuTempC}<C7>C ",
                $"<C5>C <C3>{m.CpuTempC}<C7>C",
                " <C8>| ",
                $"<C6>{powerTag}<C> ");

            int cols = QuantizeCardCols(MeasureMaxVisibleLineChars(body) + 2, minCols: 42);
            return new RtssOsdPayload(
                BuildSingleLayerCard(header, stickyAnchor, cols, heightLines: 1, extentOrigin: 4, body),
                null);
        }

        double vramUsedGb = m.VramUsedMb / 1024.0;
        double vramTotalGb = m.VramTotalMb > 0 ? m.VramTotalMb / 1024.0 : 16.0;
        string fanChip = m.FanRpm > 0 ? $" <C8>| <C3>{m.FanRpm}<C7>R" : string.Empty;

        if (clampedLevel == 2)
        {
            // Compact 1-line horizontal handheld telemetry bar (fits cleanly at 1080p & 720p)
            string body = string.Concat(
                " <C2><FR> <C7>FPS",
                " <C8>| ",
                $"<C7>APU <C3>{m.ApuPowerWatts}<C7>/{m.TargetTdpWatts}W",
                " <C8>| ",
                $"<C4>GPU <C3>{m.GpuUsagePercent}% {m.GpuTempC}<C7>C <C3>{vramUsedGb.ToString("F1", ic)}<C7>G",
                " <C8>| ",
                $"<C5>CPU <C3>{m.CpuUsagePercent}% {m.CpuTempC}<C7>C",
                fanChip,
                " <C8>| ",
                $"<C6>{powerTag}<C> ");

            int cols = QuantizeCardCols(MeasureMaxVisibleLineChars(body) + 2, minCols: 60);
            return new RtssOsdPayload(
                BuildSingleLayerCard(header, stickyAnchor, cols, heightLines: 1, extentOrigin: 4, body),
                null);
        }

        // Level 3 & 4: Crisp 3-line left-aligned hardware telemetry card (+ live filled frametime graph on Level 4)
        string vramStr = m.VramUsedMb > 0 || m.VramTotalMb > 0
            ? $"<C7>VRAM <C3>{vramUsedGb.ToString("F1", ic)}<C7>/{vramTotalGb.ToString("F0", ic)}G"
            : $"<C7>RAM <C3>{m.UsedRamGb.ToString("F1", ic)}G";
        string fanLine = m.FanRpm > 0 ? $"  <C7>FAN <C3>{m.FanRpm}" : string.Empty;

        var lines = new StringBuilder(380);
        lines.Append($" <C2><FR> <C7>FPS (<FT>ms)   <C7>APU <C3>{m.ApuPowerWatts}<C7>/{m.TargetTdpWatts}W   <C6>{powerTag}\n");
        lines.Append($" <C4>GPU <C3>{m.GpuUsagePercent,3}%  {m.GfxClockMhz,4}<C7>MHz  <C3>{m.GpuTempC,2}<C7>C   {vramStr}\n");
        lines.Append($" <C5>CPU <C3>{m.CpuUsagePercent,3}%  {m.CpuClockMhz,4}<C7>MHz  <C3>{m.CpuTempC,2}<C7>C   <C7>RAM <C3>{m.UsedRamGb.ToString("F1", ic)}G{fanLine}");

        int cardCols = QuantizeCardCols(MeasureMaxVisibleLineChars(lines.ToString()) + 2, minCols: 48);

        if (clampedLevel == 3)
        {
            lines.Append("<C>");
            return new RtssOsdPayload(
                BuildSingleLayerCard(header, stickyAnchor, cardCols, heightLines: 3, extentOrigin: 3, lines.ToString()),
                null);
        }

        // Level 4: append live RTSS native filled frametime graph object (<OBJ=00000000>)
        int graphCols = Math.Max(32, cardCols - 2);
        lines.Append("\n <C2><OBJ=00000000><C>");
        byte[] graphBuf = BuildNativeFrametimeGraphBuffer(widthChars: -graphCols, heightLines: -2);
        return new RtssOsdPayload(
            BuildSingleLayerCard(header, stickyAnchor, cardCols, heightLines: 5, extentOrigin: 3, lines.ToString()),
            graphBuf);
    }

    /// <summary>
    /// Emits a single universal RTSS OverlayLayer (&lt;P&gt;&lt;L0&gt;&lt;C0&gt;&lt;B=0,0&gt;\b&lt;C&gt;&lt;E&gt;content)
    /// compatible with all RTSS 7.2+ and 7.3.x releases without leaking raw tags.
    /// </summary>
    private static string BuildSingleLayerCard(
        string header,
        int stickyAnchor,
        int widthChars,
        int heightLines,
        int extentOrigin,
        string content)
    {
        string extentTag = extentOrigin == 0
            ? $"<E=-{widthChars},-{heightLines}>"
            : $"<E=-{widthChars},-{heightLines},{extentOrigin}>";

        return string.Concat(
            header,
            $"<P{stickyAnchor}><L0>",
            "<C0><B=0,0>\b<C>",
            extentTag,
            content);
    }

    public static int MeasureMaxVisibleLineChars(string hypertext)
    {
        if (string.IsNullOrEmpty(hypertext)) return 0;
        int maxCols = 0;
        int curCols = 0;
        for (int i = 0; i < hypertext.Length; i++)
        {
            char c = hypertext[i];
            if (c == '\n')
            {
                if (curCols > maxCols) maxCols = curCols;
                curCols = 0;
                continue;
            }
            if (c == '\r' || c == '\b') continue;
            if (c == '<')
            {
                int end = hypertext.IndexOf('>', i + 1);
                if (end > i)
                {
                    ReadOnlySpan<char> tag = hypertext.AsSpan(i + 1, end - i - 1);
                    if (tag.SequenceEqual("FR"))
                    {
                        curCols += 4;
                    }
                    else if (tag.SequenceEqual("FT"))
                    {
                        curCols += 5;
                    }
                    i = end;
                    continue;
                }
            }
            curCols++;
        }

        return Math.Max(maxCols, curCols);
    }

    private static int QuantizeCardCols(int rawCols, int minCols)
    {
        int clamped = Math.Max(rawCols, minCols);
        return ((clamped + 3) / 4) * 4;
    }

    private static int ResolveStickyAnchor(string? position)
    {
        return (position ?? "top-center").Trim().ToLowerInvariant() switch
        {
            "top-left" => 0,
            "top-right" => 2,
            _ => 1
        };
    }

    private static byte[] BuildNativeFrametimeGraphBuffer(int widthChars, int heightLines)
    {
        // RTSS_EMBEDDED_OBJECT_GRAPH (36 bytes when dwDataCount == 0 for native hooked frametime history).
        // Per HypertextExtension.cpp:934, frametime graph min/max are in microseconds (0..50ms => 0..50000us).
        byte[] buf = new byte[36];
        BitConverter.TryWriteBytes(buf.AsSpan(0, 4), GraphSignatureGr00);
        BitConverter.TryWriteBytes(buf.AsSpan(4, 4), 36u);
        BitConverter.TryWriteBytes(buf.AsSpan(8, 4), widthChars);
        BitConverter.TryWriteBytes(buf.AsSpan(12, 4), heightLines);
        BitConverter.TryWriteBytes(buf.AsSpan(16, 4), 1); // dwMargin = 1px
        BitConverter.TryWriteBytes(buf.AsSpan(20, 4), GraphFlagFilled | GraphFlagFrametime | GraphFlagBgnd | GraphFlagAutoscale);
        BitConverter.TryWriteBytes(buf.AsSpan(24, 4), 0.0f);
        BitConverter.TryWriteBytes(buf.AsSpan(28, 4), 50000.0f);
        BitConverter.TryWriteBytes(buf.AsSpan(32, 4), 0u);
        return buf;
    }

    private static string BuildShortPowerTag(RtssOsdMetrics m, CultureInfo ic)
    {
        string sysW = m.SystemDrawWatts.ToString("F0", ic);
        if (!m.HasBattery)
        {
            return $"AC {sysW}W";
        }

        return m.PowerRuntimeMode switch
        {
            "charging" => $"CHG {m.BatteryPercent}% {sysW}W",
            "ac_sustained" => $"AC {sysW}W {m.BatteryPercent}%",
            "ac_discharging" => $"AC -{m.BatteryDischargeWatts.ToString("F0", ic)}W {m.BatteryPercent}%",
            _ => FormatBatteryTag(m, sysW)
        };
    }

    private static string FormatBatteryTag(RtssOsdMetrics m, string sysW)
    {
        if (m.EstimatedStandbyMinutes is > 0 and var mins)
        {
            int h = mins / 60;
            int rem = mins % 60;
            string eta = h > 0 ? $"{h}h{rem:D2}m" : $"{rem}m";
            return $"BAT {m.BatteryPercent}% -{sysW}W {eta}";
        }

        return $"BAT {m.BatteryPercent}% -{sysW}W";
    }
}
