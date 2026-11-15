using System.Buffers.Binary;

namespace HandheldOpti.Core.Models;

public sealed class GpdControllerConfig
{
    public bool Connected { get; set; }
    public string FirmwareX { get; set; } = string.Empty;
    public string FirmwareK { get; set; } = string.Empty;
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

    public static string DescribeHidKey(ushort code, int mode, int slots)
    {
        if (mode == 2)
        {
            string first = KnownHidLabels.TryGetValue(code, out var lbl) ? lbl : $"0x{code:X4}";
            return $"组合宏 ({slots}槽: {first}...)";
        }
        return KnownHidLabels.TryGetValue(code, out var label) ? label : $"HID 0x{code:X4}";
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
        data[modeDataOffset] = 0; // Single key pass-through mode
        data[slotsDataOffset] = hidCode == 0 ? (byte)0 : (byte)1;

        int slotByteStart = HeaderSize + slot1U16Pos * 2;
        BinaryPrimitives.WriteUInt16LittleEndian(configBuf.Slice(slotByteStart, 2), hidCode);
        BinaryPrimitives.WriteUInt16LittleEndian(configBuf.Slice(slotByteStart + 2, 2), 0); // startDelayMs
        BinaryPrimitives.WriteUInt16LittleEndian(configBuf.Slice(slotByteStart + 4, 2), hidCode == 0 ? (ushort)0 : (ushort)50); // holdDurationMs

        // Clear remaining 3 slots cleanly
        for (int s = 1; s < 4; s++)
        {
            int pos = slotByteStart + s * 6;
            configBuf.Slice(pos, 6).Clear();
        }
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
