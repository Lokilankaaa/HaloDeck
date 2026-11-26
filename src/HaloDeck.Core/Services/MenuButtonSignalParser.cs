using System.Text;

namespace HaloDeck.Core.Services;

/// <summary>
/// Parses GPD Win 5 Controller V2 vendor HID interrupt reports (VID_2F24, UsagePage 0xFF00, 64B)
/// matching GPDTool.exe's native reader (ReportID=0x01, Magic=0xA5|0xB4, KeyByte[8]=0x68).
/// </summary>
public sealed class GpdVendorHidMenuParser
{
    public const byte ReportId = 0x01;
    public const byte MagicA5 = 0xA5;
    public const byte MagicB4 = 0xB4;
    public const byte MenuHidUsageF13 = 0x68;

    private readonly object _sync = new();
    private byte _prevKeyByte;

    public static bool TryExtractMenuKeyByte(ReadOnlySpan<byte> report, out byte keyByte)
    {
        keyByte = 0;
        if (report.Length < 9)
        {
            return false;
        }

        if (report[0] != ReportId)
        {
            return false;
        }

        if (report[1] != MagicA5 && report[1] != MagicB4)
        {
            return false;
        }

        keyByte = report[8];
        return true;
    }

    /// <summary>
    /// Feeds a raw 64-byte GPD Vendor HID input report and returns true on the rising edge
    /// of the physical Menu/F13 key (prevByte[8] == 0 &amp;&amp; curByte[8] == 0x68).
    /// </summary>
    public bool ProcessReport(ReadOnlySpan<byte> report)
    {
        if (!TryExtractMenuKeyByte(report, out byte keyByte))
        {
            return false;
        }

        lock (_sync)
        {
            bool pressed = _prevKeyByte == 0 && keyByte == MenuHidUsageF13;
            _prevKeyByte = keyByte;
            return pressed;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _prevKeyByte = 0;
        }
    }
}

/// <summary>
/// Matches Raw Input keyboard events (WM_INPUT RIM_TYPEKEYBOARD) for physical Menu/F13 signals,
/// including extended scancode 0x64 when VKey is unmapped (0xFF).
/// </summary>
public static class RawKeyboardMenuMatcher
{
    public const ushort RiKeyBreak = 0x0001;
    public const ushort VkF13 = 0x7C;
    public const ushort ScanF13 = 0x64;

    public static bool IsF13KeyDown(ushort vKey, ushort makeCode, ushort flags)
    {
        if ((flags & RiKeyBreak) != 0)
        {
            return false;
        }

        return vKey == VkF13 || (vKey == 0x00FF && makeCode == ScanF13);
    }
}

public sealed record KeyCaptureEventRecord(
    long OffsetMs,
    string Channel,
    string Summary,
    string RawHex,
    bool WouldTriggerQam);

public sealed record KeyCaptureEnvironmentInfo(
    bool SelfElevated,
    bool RegisterHotKeyF13Ok,
    int RegisterHotKeyF13Error,
    bool GpdVendorHidConnected,
    string GpdVendorHidDevice,
    string GpdVendorHidError,
    string XInputBackend,
    int ConnectedXInputCount,
    bool GpdToolRunning,
    bool GpdToolElevated,
    bool GpdToolServiceRunning,
    string FirmwareX,
    string FirmwareK,
    bool InterceptPhysicalMenuButton,
    bool InterceptGuideMenuButton);

public sealed record KeyCaptureSessionReport(
    int DurationMs,
    KeyCaptureEnvironmentInfo Environment,
    IReadOnlyList<KeyCaptureEventRecord> Events,
    string CopyableReport);

/// <summary>
/// Thread-safe 5-second diagnostic collector capturing signals across GPD Vendor HID,
/// Raw Input (Keyboard/Consumer/HID), WH_KEYBOARD_LL, RegisterHotKey, GetAsyncKeyState, and XInputEx.
/// </summary>
public sealed class KeyCaptureDiagnosticSession
{
    private const int MaxCapturedEvents = 200;

    private readonly object _sync = new();
    private readonly long _startTickMs;
    private readonly List<KeyCaptureEventRecord> _events = new();

    public KeyCaptureDiagnosticSession(long startTickMs)
    {
        _startTickMs = startTickMs;
    }

    public void Record(long nowTickMs, string channel, string summary, string rawHex = "", bool wouldTriggerQam = false)
    {
        lock (_sync)
        {
            if (_events.Count >= MaxCapturedEvents)
            {
                return;
            }

            long offset = Math.Max(0, nowTickMs - _startTickMs);
            _events.Add(new KeyCaptureEventRecord(offset, channel, summary, rawHex, wouldTriggerQam));
        }
    }

    public KeyCaptureSessionReport BuildReport(KeyCaptureEnvironmentInfo env, int durationMs)
    {
        List<KeyCaptureEventRecord> snapshot;
        lock (_sync)
        {
            snapshot = _events.ToList();
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== HaloDeck 实体菜单键抓包诊断报告 ===");
        sb.AppendLine($"抓包时长: {durationMs} ms | 捕获事件数: {snapshot.Count}");
        sb.AppendLine($"HaloDeck 权限: {(env.SelfElevated ? "管理员 (High IL)" : "普通用户态 (Medium IL)")}");
        sb.AppendLine($"硬件开关: 菜单键拦截={(env.InterceptPhysicalMenuButton ? "开" : "关")} | Guide(0x0400)={(env.InterceptGuideMenuButton ? "开" : "关")}");
        sb.AppendLine($"GPD Vendor HID (0xFF00): {(env.GpdVendorHidConnected ? $"已连接 ({env.GpdVendorHidDevice})" : $"未连接 ({(string.IsNullOrWhiteSpace(env.GpdVendorHidError) ? "未找到设备" : env.GpdVendorHidError)})")}");
        sb.AppendLine($"RegisterHotKey(F13): {(env.RegisterHotKeyF13Ok ? "注册成功" : $"被占用或失败 (Win32={env.RegisterHotKeyF13Error})")}");
        sb.AppendLine($"XInput 后端: {env.XInputBackend} | 已连接手柄数: {env.ConnectedXInputCount}");
        sb.AppendLine($"GPD 手柄固件: X={NullDash(env.FirmwareX)} / K={NullDash(env.FirmwareK)}");
        sb.AppendLine($"GPDTool.exe: {(env.GpdToolRunning ? (env.GpdToolElevated ? "运行中 (管理员)" : "运行中 (普通权限)") : "未运行")} | GPDToolService: {(env.GpdToolServiceRunning ? "运行中" : "未运行")}");
        sb.AppendLine("--- 捕获信号明细 ---");
        if (snapshot.Count == 0)
        {
            sb.AppendLine("(5秒内未检测到任何键盘 / HID / XInput 原始信号)");
        }
        else
        {
            foreach (var ev in snapshot)
            {
                string triggerTag = ev.WouldTriggerQam ? " [可触发侧栏]" : "";
                string hexPart = string.IsNullOrWhiteSpace(ev.RawHex) ? "" : $" | RAW={ev.RawHex}";
                sb.AppendLine($"+{ev.OffsetMs,4}ms [{ev.Channel}] {ev.Summary}{hexPart}{triggerTag}");
            }
        }

        return new KeyCaptureSessionReport(durationMs, env, snapshot, sb.ToString().TrimEnd());
    }

    private static string NullDash(string? s) => string.IsNullOrWhiteSpace(s) ? "--" : s;
}
