using System.Buffers.Binary;
using HandheldOpti.Core.Config;
using HandheldOpti.Core.Models;
using HandheldOpti.Core.Services;
using Xunit;

namespace HandheldOpti.Tests;

public sealed class HandheldV032Tests
{
    [Fact]
    public void ZenThermalDecoder_DecodesTctlWithAndWithoutOffsetBit()
    {
        Assert.Equal(0, ZenThermalDecoder.DecodeTctlTempC(0));
        Assert.Equal(0, ZenThermalDecoder.DecodeTctlTempC(uint.MaxValue));

        // 65.0C without bit 19 range offset => (65 / 0.125) = 520 << 21
        uint rawNoOffset = 520u << 21;
        Assert.Equal(65, ZenThermalDecoder.DecodeTctlTempC(rawNoOffset));

        // 64.0C with bit 19 (-49C offset) => (64 + 49) / 0.125 = 904 << 21 | (1 << 19)
        uint rawWithOffset = (904u << 21) | (1u << 19);
        Assert.Equal(64, ZenThermalDecoder.DecodeTctlTempC(rawWithOffset));

        // Unrealistic low or high register values return 0
        Assert.Equal(0, ZenThermalDecoder.DecodeTctlTempC((16u << 21) | (1u << 19))); // 2 - 49 = -47C
        Assert.Equal(0, ZenThermalDecoder.DecodeTctlTempC(1200u << 21)); // 150C > 125C bound
    }

    [Fact]
    public void GpdV2PacketCodec_BuildsValidV2PacketsAndValidatesResponses()
    {
        var init1 = GpdV2PacketCodec.BuildV2Init1Report();
        Assert.Equal(GpdV2PacketCodec.ReportSize, init1.Length);
        Assert.Equal(0x01, init1[0]);
        Assert.Equal(0x21, init1[1]);
        Assert.Equal((byte)GpdV2PacketCodec.V2Init1Payload.Length, init1[2]);
        Assert.Equal(
            GpdV2PacketCodec.ComputeBytesSum(GpdV2PacketCodec.V2Init1Payload),
            BinaryPrimitives.ReadUInt16LittleEndian(init1.AsSpan(6, 2)));

        var init2 = GpdV2PacketCodec.BuildV2Init2Report();
        Assert.Equal(0x01, init2[0]);
        Assert.Equal(0x2B, init2[1]);
        Assert.Equal((byte)GpdV2PacketCodec.V2Init2Payload.Length, init2[2]);
        Assert.Equal(
            GpdV2PacketCodec.ComputeBytesSum(GpdV2PacketCodec.V2Init2Payload),
            BinaryPrimitives.ReadUInt16LittleEndian(init2.AsSpan(6, 2)));

        byte[] chunk = [10, 20, 30, 40, 50];
        var writePkt = GpdV2PacketCodec.BuildV2WritePageReport(0x0140, chunk);
        Assert.Equal(0x43, writePkt[1]);
        Assert.Equal(5, writePkt[2]);
        Assert.Equal(0x0140, BinaryPrimitives.ReadUInt16LittleEndian(writePkt.AsSpan(4, 2)));
        Assert.Equal(150, BinaryPrimitives.ReadUInt16LittleEndian(writePkt.AsSpan(6, 2)));

        var resp = new byte[GpdV2PacketCodec.ReportSize];
        resp[8] = 0xAA;
        resp[9] = 0x55;
        BinaryPrimitives.WriteUInt16LittleEndian(
            resp.AsSpan(6, 2),
            GpdV2PacketCodec.ComputeBytesSum(resp.AsSpan(8, GpdV2PacketCodec.ReportSize - 8)));
        Assert.True(GpdV2PacketCodec.IsValidV2Response(resp));

        // Corrupt checksum fails validation
        resp[6] ^= 0x01;
        Assert.False(GpdV2PacketCodec.IsValidV2Response(resp));

        // Controller error marker 0xE2 fails even if checksum matches
        resp[8] = 0xE2;
        BinaryPrimitives.WriteUInt16LittleEndian(
            resp.AsSpan(6, 2),
            GpdV2PacketCodec.ComputeBytesSum(resp.AsSpan(8, GpdV2PacketCodec.ReportSize - 8)));
        Assert.False(GpdV2PacketCodec.IsValidV2Response(resp));
    }

    [Fact]
    public void GpdV2PacketCodec_WritesXInputBackButtonsAndPreservesMacros()
    {
        var buf = new byte[GpdV2PacketCodec.ConfigBufferSize];

        // Seed existing L4 multi-key macro (mode 6, 3 slots) and trailing data to verify preservation / clearing
        buf[GpdV2PacketCodec.HeaderSize + 160] = 6;
        buf[GpdV2PacketCodec.HeaderSize + 161] = 3;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(GpdV2PacketCodec.HeaderSize + 82 * 2, 2), 0x002C);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(GpdV2PacketCodec.HeaderSize + 82 * 2 + 6, 2), 0x0028);

        // First update leaves L4 untouched (null) and sets R4 to native XInput R3 (0x8010)
        GpdV2PacketCodec.ApplyUpdateInPlace(buf, new GpdControllerUpdateRequest
        {
            L4SingleHidCode = null,
            R4SingleHidCode = 0x8010,
            RumbleLevel = 2,
            LeftDeadzone = -3,
            RightDeadzone = 4
        });

        var afterMacroKeep = GpdV2PacketCodec.Decode(buf, "1.0", "2.0");
        Assert.True(afterMacroKeep.ChecksumValid);
        Assert.Equal(6, afterMacroKeep.L4Mode);
        Assert.Equal(3, afterMacroKeep.L4Slots);
        Assert.Contains("多键组合宏", afterMacroKeep.L4KeyLabel);
        Assert.Equal(0, afterMacroKeep.R4Mode);
        Assert.Equal(1, afterMacroKeep.R4Slots);
        Assert.Equal((ushort)0x8010, afterMacroKeep.R4KeyCode);
        Assert.Contains("R3", afterMacroKeep.R4KeyLabel);
        Assert.Equal(2, afterMacroKeep.RumbleLevel);
        Assert.Equal(-3, afterMacroKeep.LeftDeadzone);
        Assert.Equal(4, afterMacroKeep.RightDeadzone);

        // Second update sets L4 to native XInput L3 (0x800F) and clears macro slot 2
        GpdV2PacketCodec.ApplyUpdateInPlace(buf, new GpdControllerUpdateRequest
        {
            L4SingleHidCode = 0x800F,
            R4SingleHidCode = 0x8010,
            RumbleLevel = 1,
            LeftDeadzone = 0,
            RightDeadzone = 0
        });

        var decoded = GpdV2PacketCodec.Decode(buf);
        Assert.True(decoded.ChecksumValid);
        Assert.Equal(0, decoded.L4Mode);
        Assert.Equal(1, decoded.L4Slots);
        Assert.Equal((ushort)0x800F, decoded.L4KeyCode);
        Assert.Contains("L3", decoded.L4KeyLabel);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(GpdV2PacketCodec.HeaderSize + 82 * 2 + 6, 2)));
    }

    [Fact]
    public void SettingsStore_NormalizesAndRoundtripsV032OskAndBackButtonSettings()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "HaloDeck_V032_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new SettingsStore(tempDir);

            // Legacy "xbox", "touch", and unknown modes normalize to GPD Tool 1.66's "tabtip"
            var raw = new AppSettings
            {
                InterceptPhysicalOskButton = false,
                PreferredOskMode = "UNSUPPORTED_MODE",
                EnableBackButtonHotkeys = false
            };
            store.Save(raw);
            var loadedDefaultOsk = store.Load();
            Assert.False(loadedDefaultOsk.InterceptPhysicalOskButton);
            Assert.Equal("tabtip", loadedDefaultOsk.PreferredOskMode);
            Assert.False(loadedDefaultOsk.EnableBackButtonHotkeys);

            foreach (var legacyMode in new[] { "xbox", "TOUCH" })
            {
                raw.PreferredOskMode = legacyMode;
                store.Save(raw);
                Assert.Equal("tabtip", store.Load().PreferredOskMode);
            }

            // Explicit "osk" mode still roundtrips cleanly
            raw.PreferredOskMode = "OSK";
            raw.InterceptPhysicalOskButton = true;
            raw.EnableBackButtonHotkeys = true;
            store.Save(raw);

            var reloaded = store.Load();
            Assert.True(reloaded.InterceptPhysicalOskButton);
            Assert.Equal("osk", reloaded.PreferredOskMode);
            Assert.True(reloaded.EnableBackButtonHotkeys);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void OskToggleStateGuard_PreventsBounceOnAutoDismissAndAllowsCleanReopen()
    {
        var guard = new OskToggleStateGuard();

        // Initially closed
        Assert.False(guard.CachedVisible);
        Assert.False(guard.ShouldCloseFromSnapshot(900));
        Assert.False(guard.ShouldCloseWithLiveQuery(liveVisible: false, nowTicks: 900));

        // Keyboard opens and monitor observes visible at t = 1000
        guard.BeginIntentionalOpen();
        guard.RecordSample(isVisible: true, nowTicks: 1000);
        Assert.True(guard.CachedVisible);
        Assert.True(guard.ShouldCloseFromSnapshot(1050));

        // Physical Ctrl+Win or mouse pointer-down auto-dismisses TabTip right before toggle executes (t = 1120)
        guard.RecordSample(isVisible: false, nowTicks: 1120);
        Assert.False(guard.CachedVisible);
        Assert.True(guard.ShouldCloseFromSnapshot(1120));
        Assert.True(guard.ShouldCloseWithLiveQuery(liveVisible: false, nowTicks: 1120));

        // Close completes cleanly at t = 1400; 3rd press at t = 1600 must open (not treat as auto-dismiss)
        guard.BeginIntentionalClose(1130);
        guard.CompleteIntentionalClose(1400, postCloseSuppressMs: 350);
        Assert.False(guard.ShouldCloseFromSnapshot(1600));
        Assert.False(guard.ShouldCloseWithLiveQuery(liveVisible: false, nowTicks: 1600));

        // Stale visibility sample from an earlier session beyond the grace window must open cleanly
        guard.BeginIntentionalOpen();
        guard.RecordSample(isVisible: true, nowTicks: 3000);
        guard.RecordSample(isVisible: false, nowTicks: 3050);
        Assert.False(guard.ShouldCloseFromSnapshot(4000));
        Assert.False(guard.ShouldCloseWithLiveQuery(liveVisible: false, nowTicks: 4000));
    }
}
