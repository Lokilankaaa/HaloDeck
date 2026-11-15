using System.Buffers.Binary;
using System.Text.Json;
using HandheldOpti.Core.Models;
using Xunit;

namespace HandheldOpti.Tests;

public sealed class HandheldV030Tests
{
    [Fact]
    public void AppSettings_V030DefaultsAndLegacyJsonDeserialization()
    {
        var defaults = new AppSettings();
        Assert.Equal(40, defaults.CurrentTdpWatts);
        Assert.Equal(18, defaults.TdpPresetSilent);
        Assert.Equal(40, defaults.TdpPresetBalanced);
        Assert.Equal(80, defaults.TdpPresetTurbo);
        Assert.Equal(0, defaults.OverlayLevel);
        Assert.Equal("auto", defaults.FanMode);
        Assert.NotNull(defaults.PerGameProfiles);

        defaults.PerGameProfiles["GameA"] = new GamePerformanceProfile
        {
            GameId = "GameA",
            GameName = "Test Game",
            Enabled = true,
            TdpWatts = 55,
            RefreshRateHz = 120
        };
        Assert.True(defaults.PerGameProfiles.ContainsKey("gamea"));

        const string legacyV02Json = """
        {
          "activeOptiScalerSlot": "OptiScalerStable",
          "defaultPresetId": "xess-antishimmer",
          "backgroundAutoInjectEnabled": true
        }
        """;

        var migrated = JsonSerializer.Deserialize<AppSettings>(legacyV02Json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(migrated);
        Assert.Equal("OptiScalerStable", migrated!.ActiveOptiScalerSlot);
        Assert.Equal("xess-antishimmer", migrated.DefaultPresetId);
        Assert.True(migrated.BackgroundAutoInjectEnabled);
        Assert.Equal(40, migrated.CurrentTdpWatts);
        Assert.Equal(18, migrated.TdpPresetSilent);
        Assert.Equal(40, migrated.TdpPresetBalanced);
        Assert.Equal(80, migrated.TdpPresetTurbo);
        Assert.Equal(0, migrated.OverlayLevel);
        Assert.NotNull(migrated.PerGameProfiles);
    }

    [Fact]
    public void GpdV2PacketCodec_ChecksumComputeUpdateAndTamperDetection()
    {
        var buf = new byte[GpdV2PacketCodec.ConfigBufferSize];
        for (int i = GpdV2PacketCodec.HeaderSize; i < buf.Length; i++)
        {
            buf[i] = (byte)((i * 37) & 0xFF);
        }

        ushort expected = GpdV2PacketCodec.ComputeChecksum(buf);
        GpdV2PacketCodec.UpdateChecksumInPlace(buf);

        Assert.Equal(expected, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(4, 2)));
        Assert.Equal((ushort)(expected ^ 0xFFFF), BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(8, 2)));
        Assert.True(GpdV2PacketCodec.VerifyChecksum(buf));

        // Mutate one byte inside payload -> checksum must fail
        buf[200] ^= 0x01;
        Assert.False(GpdV2PacketCodec.VerifyChecksum(buf));
    }

    [Fact]
    public void GpdV2PacketCodec_ApplyUpdateAndDecodeRoundTrip()
    {
        var buf = new byte[GpdV2PacketCodec.ConfigBufferSize];
        GpdV2PacketCodec.UpdateChecksumInPlace(buf);

        var req = new GpdControllerUpdateRequest
        {
            L4SingleHidCode = 0x0068, // F13
            R4SingleHidCode = 0x0069, // F14
            RumbleLevel = 2,
            LeftDeadzone = -4,
            RightDeadzone = 6
        };

        GpdV2PacketCodec.ApplyUpdateInPlace(buf, req);
        Assert.True(GpdV2PacketCodec.VerifyChecksum(buf));

        var decoded = GpdV2PacketCodec.Decode(buf, "1.0", "2.0");
        Assert.True(decoded.Connected);
        Assert.True(decoded.ChecksumValid);
        Assert.Equal(0, decoded.L4Mode);
        Assert.Equal(1, decoded.L4Slots);
        Assert.Equal((ushort)0x0068, decoded.L4KeyCode);
        Assert.Contains("F13", decoded.L4KeyLabel);
        Assert.Equal(0, decoded.R4Mode);
        Assert.Equal(1, decoded.R4Slots);
        Assert.Equal((ushort)0x0069, decoded.R4KeyCode);
        Assert.Contains("F14", decoded.R4KeyLabel);
        Assert.Equal(2, decoded.RumbleLevel);
        Assert.Equal(-4, decoded.LeftDeadzone);
        Assert.Equal(6, decoded.RightDeadzone);
    }

    [Fact]
    public void GpdV2PacketCodec_PreservesExistingMacroWhenSingleCodeIsNull()
    {
        var buf = new byte[GpdV2PacketCodec.ConfigBufferSize];
        // Seed L4 factory multi-step macro inside configData (offset 12+)
        buf[GpdV2PacketCodec.HeaderSize + 160] = 2; // L4Mode = macro
        buf[GpdV2PacketCodec.HeaderSize + 161] = 3; // L4Slots = 3
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(GpdV2PacketCodec.HeaderSize + 82 * 2, 2), 0x0010); // M
        GpdV2PacketCodec.UpdateChecksumInPlace(buf);

        var req = new GpdControllerUpdateRequest
        {
            L4SingleHidCode = null, // keep factory macro
            R4SingleHidCode = 0x0049, // Insert
            RumbleLevel = 1,
            LeftDeadzone = 0,
            RightDeadzone = 0
        };

        GpdV2PacketCodec.ApplyUpdateInPlace(buf, req);
        Assert.True(GpdV2PacketCodec.VerifyChecksum(buf));

        var decoded = GpdV2PacketCodec.Decode(buf);
        Assert.Equal(2, decoded.L4Mode);
        Assert.Equal(3, decoded.L4Slots);
        Assert.Equal((ushort)0x0010, decoded.L4KeyCode);
        Assert.Contains("组合宏", decoded.L4KeyLabel);

        Assert.Equal(0, decoded.R4Mode);
        Assert.Equal((ushort)0x0049, decoded.R4KeyCode);
        Assert.Contains("Insert", decoded.R4KeyLabel);
    }
}
