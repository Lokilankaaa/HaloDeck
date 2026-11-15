using System.Buffers.Binary;

namespace HandheldOpti.Core.Models;

public sealed class GpdControllerConfig
{
    public bool Connected { get; set; }
    public string FirmwareX { get; set; } = string.Empty;
    public string FirmwareK { get; set; } = string.Empty;
    public int EmulationMode { get; set; }
    public int L4Mode { get; set; }
    public int L4Slots { get; set; }
    public ushort L4KeyCode { get; set; }
    public string L4KeyLabel { get; set; } = string.Empty;
    public int R4Mode { get; set; }
    public int R4Slots { get; set; }
    public ushort R4KeyCode { get; set; }
    public string R4KeyLabel { get; set; } = string.Empty;
    public int RumbleLevel { get; set; } = 1; // 0=Off, 1=Low, 2=High
    public int LeftDeadzone { get; set; }
    public int RightDeadzone { get; set; }
    public bool ChecksumValid { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}

public sealed class GpdControllerUpdateRequest
{
    /// <summary>
    /// Null or negative keeps the existing L4 configuration (preserves factory multi-step macros).
    /// Non-negative writes Single key mode with the given HID Usage ID (0x00 = unbound).
    /// </summary>
    public int? L4SingleHidCode { get; set; }

    /// <summary>
    /// Null or negative keeps the existing R4 configuration.
    /// Non-negative writes Single key mode with the given HID Usage ID (0x00 = unbound).
    /// </summary>
    public int? R4SingleHidCode { get; set; }

    public int RumbleLevel { get; set; } = 1;
    public int LeftDeadzone { get; set; }
    public int RightDeadzone { get; set; }
}

/// <summary>
/// Pure encoder/decoder and checksum calculator for the GPD Win 5 Controller V2 (VID_2F24&PID_0137)
/// 1024-byte onboard configuration block, based on OpenWinControls documented offsets.
/// </summary>
public static class GpdV2PacketCodec
{
    public const int ConfigBufferSize = 1024;
    public const int HeaderSize = 12;
    public const int ReportSize = 64;

    // Back-button offsets inside configData (= configBuf + HeaderSize)
    private const int L4ModeOffset = 160;
    private const int L4SlotsOffset = 161;
    private const int L4Slot1U16Pos = 82;

    private const int R4ModeOffset = 356;
    private const int R4SlotsOffset = 357;
    private const int R4Slot1U16Pos = 180;

    private const int RumbleOffset = 944;
    private const int LeftDeadzoneOffset = 949;
    private const int RightDeadzoneOffset = 951;
    private const int EmulationModeOffset = 959;

    private static readonly IReadOnlyDictionary<ushort, string> KnownHidLabels = new Dictionary<ushort, string>
    {
        [0x0000] = "无 / 未映射",
        [0x0068] = "F13（HaloDeck 侧边栏）",
        [0x0069] = "F14（HaloDeck 性能 OSD）",
        [0x006A] = "F15（OptiScaler Insert）",
        [0x0049] = "Insert（呼出插件菜单）",
        [0x0046] = "PrintScreen（截图）",
        [0x0029] = "Esc",
        [0x0028] = "Enter",
        [0x002C] = "Space",
        [0x002B] = "Tab",
        [0x003A] = "F1",
        [0x003B] = "F2",
        [0x003C] = "F3",
        [0x003D] = "F4",
        [0x003E] = "F5",
        [0x003F] = "F6",
        [0x0040] = "F7",
        [0x0041] = "F8",
        [0x0042] = "F9",
        [0x0043] = "F10",
        [0x0044] = "F11",
        [0x0045] = "F12",
        [0x00E0] = "Left Ctrl",
        [0x00E1] = "Left Shift",
        [0x00E2] = "Left Alt",
        [0x00E3] = "Left Win",
        [0x0010] = "M",
        [0x0012] = "O",
        [0x000C] = "I"
    };

    private static readonly Dictionary<ushort, string> V2ExtendedKeyLabels = new()
    {
        [0x0000] = "未映射",
        // XInput native gamepad buttons (libOpenWinControls XinputV2UsageIDMap)
        [0x8000] = "手柄 ↑ (D-Pad Up)",
        [0x8001] = "手柄 ↓ (D-Pad Down)",
        [0x8002] = "手柄 ← (D-Pad Left)",
        [0x8003] = "手柄 → (D-Pad Right)",
        [0x8004] = "手柄 Start",
        [0x8005] = "手柄 Select",
        [0x8006] = "Xbox / Guide 键",
        [0x8007] = "手柄 A",
        [0x8008] = "手柄 B",
        [0x8009] = "手柄 X",
        [0x800A] = "手柄 Y",
        [0x800B] = "手柄 LB (L1)",
        [0x800C] = "手柄 RB (R1)",
        [0x800D] = "手柄 LT (L2)",
        [0x800E] = "手柄 RT (R2)",
        [0x800F] = "手柄 L3 (左摇杆按下)",
        [0x8010] = "手柄 R3 (右摇杆按下)",
        // Common PC game action & hotkeys (USB HID Usage Page 0x07)
        [0x0004] = "A",
        [0x0005] = "B",
        [0x0006] = "C",
        [0x0008] = "E (互动)",
        [0x0009] = "F (互动)",
        [0x000A] = "G",
        [0x0010] = "M (地图)",
        [0x0014] = "Q",
        [0x0015] = "R (换弹)",
        [0x0019] = "V (近战)",
        [0x0028] = "Enter",
        [0x0029] = "Esc",
        [0x002B] = "Tab",
        [0x002C] = "Space (跳跃/确认)",
        [0x003A] = "F1",
        [0x003B] = "F2",
        [0x003C] = "F3",
        [0x003D] = "F4",
        [0x0044] = "F11",
        [0x0045] = "F12",
        [0x0049] = "Insert (菜单)",
        [0x0068] = "F13 (HaloDeck 侧栏)",
        [0x0069] = "F14 (HaloDeck OSD)",
        [0x006A] = "F15 (OptiScaler 菜单)",
        [0x00E0] = "LCtrl",
        [0x00E1] = "LShift (冲刺)",
        [0x00E2] = "LAlt"
    };

    public static string ResolveKeyLabel(ushort code)
    {
        if (V2ExtendedKeyLabels.TryGetValue(code, out var ext))
        {
            return ext;
        }
        return KnownHidLabels.TryGetValue(code, out var label) ? label : $"HID 0x{code:X4}";
    }

    public static string DescribeHidKey(ushort code, int mode, int slots)
    {
        if (mode is 2 or 6)
        {
            string first = ResolveKeyLabel(code);
            return $"多键组合宏 ({slots}槽: {first}...)";
        }
        return ResolveKeyLabel(code);
    }

    public static ushort ComputeChecksum(ReadOnlySpan<byte> configBuf)
    {
        if (configBuf.Length != ConfigBufferSize)
        {
            throw new ArgumentException($"Expected {ConfigBufferSize}-byte buffer.", nameof(configBuf));
        }

        uint sum = 0;
        for (int i = HeaderSize; i < ConfigBufferSize; i++)
        {
            sum += configBuf[i];
        }
        return (ushort)(sum & 0xFFFF);
    }

    public static bool VerifyChecksum(ReadOnlySpan<byte> configBuf)
    {
        if (configBuf.Length != ConfigBufferSize) return false;
        ushort expected = ComputeChecksum(configBuf);
        ushort stored = BinaryPrimitives.ReadUInt16LittleEndian(configBuf.Slice(4, 2));
        ushort storedInv = BinaryPrimitives.ReadUInt16LittleEndian(configBuf.Slice(8, 2));
        return stored == expected && storedInv == (ushort)(expected ^ 0xFFFF);
    }

    public static void UpdateChecksumInPlace(Span<byte> configBuf)
    {
        ushort sum = ComputeChecksum(configBuf);
        BinaryPrimitives.WriteUInt16LittleEndian(configBuf.Slice(4, 2), sum);
        BinaryPrimitives.WriteUInt16LittleEndian(configBuf.Slice(8, 2), (ushort)(sum ^ 0xFFFF));
    }

    public static GpdControllerConfig Decode(ReadOnlySpan<byte> configBuf, string fwX = "", string fwK = "")
    {
        if (configBuf.Length != ConfigBufferSize)
        {
            throw new ArgumentException($"Expected {ConfigBufferSize}-byte buffer.", nameof(configBuf));
        }

        var data = configBuf.Slice(HeaderSize);
        int l4Mode = data[L4ModeOffset];
        int l4Slots = data[L4SlotsOffset];
        ushort l4Key = BinaryPrimitives.ReadUInt16LittleEndian(configBuf.Slice(HeaderSize + L4Slot1U16Pos * 2, 2));

        int r4Mode = data[R4ModeOffset];
        int r4Slots = data[R4SlotsOffset];
        ushort r4Key = BinaryPrimitives.ReadUInt16LittleEndian(configBuf.Slice(HeaderSize + R4Slot1U16Pos * 2, 2));

        return new GpdControllerConfig
        {
            Connected = true,
            FirmwareX = fwX,
            FirmwareK = fwK,
            EmulationMode = data[EmulationModeOffset],
            L4Mode = l4Mode,
            L4Slots = l4Slots,
            L4KeyCode = l4Key,
            L4KeyLabel = DescribeHidKey(l4Key, l4Mode, l4Slots),
            R4Mode = r4Mode,
            R4Slots = r4Slots,
            R4KeyCode = r4Key,
            R4KeyLabel = DescribeHidKey(r4Key, r4Mode, r4Slots),
            RumbleLevel = Math.Clamp((int)data[RumbleOffset], 0, 2),
            LeftDeadzone = Math.Clamp((int)(sbyte)data[LeftDeadzoneOffset], -10, 10),
            RightDeadzone = Math.Clamp((int)(sbyte)data[RightDeadzoneOffset], -10, 10),
            ChecksumValid = VerifyChecksum(configBuf)
        };
    }

    public static void ApplyUpdateInPlace(Span<byte> configBuf, GpdControllerUpdateRequest update)
    {
        if (configBuf.Length != ConfigBufferSize)
        {
            throw new ArgumentException($"Expected {ConfigBufferSize}-byte buffer.", nameof(configBuf));
        }

        var data = configBuf.Slice(HeaderSize);

        if (update.L4SingleHidCode is >= 0 and <= 0xFFFF)
        {
            WriteSingleBackButton(configBuf, L4ModeOffset, L4SlotsOffset, L4Slot1U16Pos, (ushort)update.L4SingleHidCode.Value);
        }

        if (update.R4SingleHidCode is >= 0 and <= 0xFFFF)
        {
            WriteSingleBackButton(configBuf, R4ModeOffset, R4SlotsOffset, R4Slot1U16Pos, (ushort)update.R4SingleHidCode.Value);
        }

        data[RumbleOffset] = (byte)Math.Clamp(update.RumbleLevel, 0, 2);
        data[LeftDeadzoneOffset] = unchecked((byte)(sbyte)Math.Clamp(update.LeftDeadzone, -10, 10));
        data[RightDeadzoneOffset] = unchecked((byte)(sbyte)Math.Clamp(update.RightDeadzone, -10, 10));

        UpdateChecksumInPlace(configBuf);
    }

    private static void WriteSingleBackButton(Span<byte> configBuf, int modeDataOffset, int slotsDataOffset, int slot1U16Pos, ushort hidCode)
    {
        var data = configBuf.Slice(HeaderSize);
        // Mode 0 is Single key pass-through (supports both HID and XInput 0x8000..0x8018 codes).
        data[modeDataOffset] = 0;
        data[slotsDataOffset] = hidCode == 0 ? (byte)0 : (byte)1;

        int slotByteStart = HeaderSize + slot1U16Pos * 2;
        BinaryPrimitives.WriteUInt16LittleEndian(configBuf.Slice(slotByteStart, 2), hidCode);
        BinaryPrimitives.WriteUInt16LittleEndian(configBuf.Slice(slotByteStart + 2, 2), 0); // startDelayMs
        BinaryPrimitives.WriteUInt16LittleEndian(configBuf.Slice(slotByteStart + 4, 2), 0); // holdDurationMs = 0 for live hold pass-through

        // Clear all remaining 31 key slots cleanly (32 slots total per V2 back button)
        for (int s = 1; s < 32; s++)
        {
            int pos = slotByteStart + s * 6;
            if (pos + 6 <= configBuf.Length)
            {
                configBuf.Slice(pos, 6).Clear();
            }
        }
    }

    public static readonly byte[] V2Init1Payload =
    [
        0xa2, 0xa3, 0xa0, 0xa1, 0xa6, 0xa7, 0xa4, 0xa5, 0xba, 0xbb, 0xb8, 0xb9, 0xbe, 0xbf, 0xbc, 0xbd,
        0xb2, 0xb3, 0xb0, 0xb1, 0xb6, 0xb7, 0xb4, 0xb5, 0x8a, 0x8b, 0x88, 0x89, 0x8e, 0x8f, 0x8c, 0x8d,
        0x82, 0x83, 0x80, 0x81, 0x86, 0x87, 0x84, 0x85, 0x9a, 0x9b, 0x98, 0x99, 0x9e, 0x9f, 0x9c, 0x9d,
        0x92, 0x93, 0x90, 0x91, 0x96, 0x97, 0x94, 0x95
    ];

    public static readonly byte[] V2Init2Payload = [0xd0, 0x07, 0x00, 0x00];

    public static ushort ComputeBytesSum(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            sum += data[i];
        }
        return unchecked((ushort)sum);
    }

    public static void UpdateConfigHeaderChecksum(Span<byte> configBuf)
    {
        if (configBuf.Length < ConfigBufferSize) return;
        ushort sum = ComputeBytesSum(configBuf.Slice(HeaderSize, ConfigBufferSize - HeaderSize));
        BinaryPrimitives.WriteUInt16LittleEndian(configBuf.Slice(4, 2), sum);
        BinaryPrimitives.WriteUInt16LittleEndian(configBuf.Slice(8, 2), (ushort)(sum ^ 0xFFFF));
    }

    public static byte[] BuildV2Init1Report()
    {
        var pkt = new byte[ReportSize];
        pkt[0] = 0x01;
        pkt[1] = 0x21;
        pkt[2] = (byte)V2Init1Payload.Length;
        V2Init1Payload.CopyTo(pkt, 8);
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(6, 2), ComputeBytesSum(V2Init1Payload));
        return pkt;
    }

    public static byte[] BuildV2Init2Report()
    {
        var pkt = new byte[ReportSize];
        pkt[0] = 0x01;
        pkt[1] = 0x2B;
        pkt[2] = (byte)V2Init2Payload.Length;
        V2Init2Payload.CopyTo(pkt, 8);
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(6, 2), ComputeBytesSum(V2Init2Payload));
        return pkt;
    }

    public static byte[] BuildV2SimpleCommand(byte cmd, byte bytesCount = 0, bool withMagic4 = false)
    {
        var pkt = new byte[ReportSize];
        pkt[0] = 0x01;
        pkt[1] = cmd;
        pkt[2] = bytesCount;
        if (withMagic4)
        {
            pkt[6] = 0x04;
            pkt[9] = 0x04;
        }
        return pkt;
    }

    public static byte[] BuildV2WritePageReport(ushort byteOffset, ReadOnlySpan<byte> chunk)
    {
        int count = Math.Min(chunk.Length, 56);
        var pkt = new byte[ReportSize];
        pkt[0] = 0x01;
        pkt[1] = 0x43;
        pkt[2] = (byte)count;
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(4, 2), byteOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(6, 2), ComputeBytesSum(chunk[..count]));
        chunk[..count].CopyTo(pkt.AsSpan(8));
        return pkt;
    }

    public static bool IsValidV2Response(ReadOnlySpan<byte> resp)
    {
        if (resp.Length < ReportSize) return false;
        if (resp[8] == 0xE2) return false;
        ushort expected = BinaryPrimitives.ReadUInt16LittleEndian(resp.Slice(6, 2));
        ushort actual = ComputeBytesSum(resp.Slice(8, ReportSize - 8));
        return expected == actual;
    }

    public static byte[] BuildCommandReport(byte cmd, byte pageLo = 0, byte pageHi = 0)
    {
        var report = new byte[64];
        report[0] = 0x01;
        report[1] = 0xA5;
        report[2] = cmd;
        report[3] = 0x5A;
        report[4] = (byte)(cmd ^ 0xFF);
        report[5] = 0x00;
        report[6] = pageLo;
        report[7] = pageHi;
        return report;
    }

    public static byte[] BuildWriteChunkReport(ReadOnlySpan<byte> configBuf, int packetIndex)
    {
        int offset = packetIndex * 52;
        int len = Math.Min(52, configBuf.Length - offset);
        var report = BuildCommandReport(0x41, (byte)(packetIndex * 2), 0);
        configBuf.Slice(offset, len).CopyTo(report.AsSpan(8, len));
        return report;
    }
}
