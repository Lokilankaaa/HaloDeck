using System.Text.Json;
using HaloDeck.Core.Models;
using HaloDeck.Core.Process;
using HaloDeck.Core.Services;
using Xunit;

namespace HaloDeck.Tests;

public sealed class SteadyEcoModeTests
{
    [Fact]
    public void CpuTopologyInspector_ClassifiesSingleCcd388_AndSelectsTailTwoCoresForBackground()
    {
        // Mirrors real Ryzen AI Max+ 388 (8 physical cores, 16 logical threads, CoreIndex 0,2..14, LLC=0, Eff=0, 32MB L3)
        var descriptors = new List<CpuSetCoreDescriptor>();
        for (int lp = 0; lp < 16; lp++)
        {
            descriptors.Add(new CpuSetCoreDescriptor(
                CpuSetId: (uint)(0x100 + lp),
                Group: 0,
                LogicalProcessorIndex: (byte)lp,
                CoreIndex: (byte)((lp / 2) * 2),
                LastLevelCacheIndex: 0,
                EfficiencyClass: 0));
        }

        var topo = CpuTopologyInspector.Analyze(descriptors, new[] { 32768 });

        Assert.Equal(CpuTopologyKind.SingleCcdHomogeneous, topo.Kind);
        Assert.Equal(8, topo.PhysicalCoreCount);
        Assert.Equal(16, topo.LogicalProcessorCount);
        Assert.Equal(32, topo.TotalL3SizeMb);
        Assert.Equal("单CCD 8核 · 32MB L3", topo.SummaryBadge);
        Assert.Equal(new uint[] { 0x10C, 0x10D, 0x10E, 0x10F }, topo.BackgroundCpuSetIds);
        Assert.Equal(12, topo.PrimaryGameCpuSetIds.Length);
    }

    [Fact]
    public void CpuTopologyInspector_ClassifiesDualCcd395_AndIsolatesSecondaryCcd()
    {
        // Ryzen AI Max+ 395: 16 cores / 32 threads across 2 LLC clusters (32MB + 32MB)
        var descriptors = new List<CpuSetCoreDescriptor>();
        for (int lp = 0; lp < 32; lp++)
        {
            byte llc = (byte)(lp < 16 ? 0 : 1);
            descriptors.Add(new CpuSetCoreDescriptor(
                CpuSetId: (uint)(0x100 + lp),
                Group: 0,
                LogicalProcessorIndex: (byte)lp,
                CoreIndex: (byte)((lp / 2) * 2),
                LastLevelCacheIndex: llc,
                EfficiencyClass: 0));
        }

        var topo = CpuTopologyInspector.Analyze(descriptors, new[] { 32768, 32768 });

        Assert.Equal(CpuTopologyKind.DualCcdHomogeneous, topo.Kind);
        Assert.Equal(16, topo.PhysicalCoreCount);
        Assert.Equal(64, topo.TotalL3SizeMb);
        Assert.Equal("2CCD 16核 · 64MB L3", topo.SummaryBadge);
        Assert.Equal(16, topo.BackgroundCpuSetIds.Length);
        Assert.All(topo.BackgroundCpuSetIds, id => Assert.True(id >= 0x110u));
        Assert.Equal(16, topo.PrimaryGameCpuSetIds.Length);
        Assert.All(topo.PrimaryGameCpuSetIds, id => Assert.True(id < 0x110u));
    }

    [Fact]
    public void CpuTopologyInspector_ClassifiesHybridHx370_AndRoutesBackgroundToZen5c()
    {
        // Strix Point HX 370 / Z2 Extreme: 4 Zen 5 (Eff=1, 16MB L3) + 8 Zen 5c (Eff=0, 8MB L3)
        var descriptors = new List<CpuSetCoreDescriptor>();
        for (int lp = 0; lp < 24; lp++)
        {
            bool isBig = lp < 8;
            descriptors.Add(new CpuSetCoreDescriptor(
                CpuSetId: (uint)(0x200 + lp),
                Group: 0,
                LogicalProcessorIndex: (byte)lp,
                CoreIndex: (byte)((lp / 2) * 2),
                LastLevelCacheIndex: (byte)(isBig ? 0 : 1),
                EfficiencyClass: (byte)(isBig ? 1 : 0)));
        }

        var topo = CpuTopologyInspector.Analyze(descriptors, new[] { 16384, 8192 });

        Assert.Equal(CpuTopologyKind.HybridBigLittle, topo.Kind);
        Assert.Equal(12, topo.PhysicalCoreCount);
        Assert.Equal(4, topo.PerformanceCoreCount);
        Assert.Equal(8, topo.EfficiencyCoreCount);
        Assert.Equal("异构 4大+8能效核 · 24MB L3", topo.SummaryBadge);
        Assert.Equal(16, topo.BackgroundCpuSetIds.Length);
        Assert.All(topo.BackgroundCpuSetIds, id => Assert.True(id >= 0x208u));
        Assert.Equal(8, topo.PrimaryGameCpuSetIds.Length);
    }

    [Fact]
    public void BackgroundEcoIsolator_NeverTouchesActiveGameOrProtectedProcesses()
    {
        const int gamePid = 4242;
        const string gameDir = @"D:\SteamLibrary\steamapps\common\Nioh3";

        // Active game PID itself is strictly excluded even if named like a browser
        Assert.False(BackgroundEcoIsolator.ShouldIsolateProcess(
            gamePid, "msedge", @"C:\Program Files\Edge\msedge.exe", gamePid, gameDir, gameDir));

        // Any helper inside the running game's install/render directory is strictly excluded
        Assert.False(BackgroundEcoIsolator.ShouldIsolateProcess(
            5150, "steamwebhelper", @"D:\SteamLibrary\steamapps\common\Nioh3\bin\steamwebhelper.exe", gamePid, gameDir, gameDir));

        // Critical OS, HaloDeck WebView2, Steam client/webhelper/overlay, and Voice Chat apps are protected
        Assert.False(BackgroundEcoIsolator.ShouldIsolateProcess(1200, "dwm", null, gamePid, gameDir, gameDir));
        Assert.False(BackgroundEcoIsolator.ShouldIsolateProcess(1201, "audiodg", null, gamePid, gameDir, gameDir));
        Assert.False(BackgroundEcoIsolator.ShouldIsolateProcess(1202, "msedgewebview2", null, gamePid, gameDir, gameDir));
        Assert.False(BackgroundEcoIsolator.ShouldIsolateProcess(1203, "Discord", null, gamePid, gameDir, gameDir));
        Assert.False(BackgroundEcoIsolator.ShouldIsolateProcess(1204, "GameOverlayUI", null, gamePid, gameDir, gameDir));
        Assert.False(BackgroundEcoIsolator.ShouldIsolateProcess(1205, "steam", @"C:\Program Files (x86)\Steam\steam.exe", gamePid, gameDir, gameDir));
        Assert.False(BackgroundEcoIsolator.ShouldIsolateProcess(1206, "steamwebhelper", @"C:\Program Files (x86)\Steam\bin\cef\cef.win7x64\steamwebhelper.exe", gamePid, gameDir, gameDir));

        // Non-Steam background launchers, browsers, and sync clutter ARE isolated
        Assert.True(BackgroundEcoIsolator.ShouldIsolateProcess(
            8801, "EpicWebHelper", @"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win64\EpicWebHelper.exe", gamePid, gameDir, gameDir));
        Assert.True(BackgroundEcoIsolator.ShouldIsolateProcess(
            8802, "msedge.exe", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", gamePid, gameDir, gameDir));
        Assert.True(BackgroundEcoIsolator.ShouldIsolateProcess(
            8803, "OneDrive", @"C:\Users\test\AppData\Local\Microsoft\OneDrive\OneDrive.exe", gamePid, gameDir, gameDir));
    }

    [Fact]
    public void PowerPeripheralOptimizer_BypassesWin11Overlay_NeverTouchesDiskWifiOrCorePark_AndCoordinatesWithCpuCap()
    {
        string tempBackup = Path.Combine(Path.GetTempPath(), $"steady_eco_test_{Guid.NewGuid():N}.json");
        var bestPerfOverlay = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");
        try
        {
            var backend = new FakePowerBackend { CurrentOverlay = bestPerfOverlay };
            // Seed exact GPD Win 5 baseline values
            backend.Seed(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingPerfEpp0, 25u, 90u);
            backend.Seed(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingPerfEpp1, 33u, 50u);
            backend.Seed(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingProcThrottleMin, 80u, 5u);
            backend.Seed(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingProcFreqMax, 0u, 0u);
            backend.Seed(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingProcFreqMax1, 0u, 0u);
            backend.Seed(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingCoreParkMin, 100u, 100u);
            backend.Seed(PowerPeripheralOptimizer.SubDisk, PowerPeripheralOptimizer.SettingPrimaryNvmeIdleMs, 200u, 30u);
            backend.Seed(PowerPeripheralOptimizer.SubDisk, PowerPeripheralOptimizer.SettingSecondaryNvmeIdleMs, 2000u, 1000u);
            backend.Seed(PowerPeripheralOptimizer.SubDisk, PowerPeripheralOptimizer.SettingAhciLinkPower, 1u, 1u);
            backend.Seed(PowerPeripheralOptimizer.SubWireless, PowerPeripheralOptimizer.SettingWirelessPowerSave, 0u, 2u);
            backend.Seed(PowerPeripheralOptimizer.SubMultimedia, PowerPeripheralOptimizer.SettingVideoQualityBias, 1u, 0u);
            backend.Seed(PowerPeripheralOptimizer.SubMultimedia, PowerPeripheralOptimizer.SettingWhenPlayingVideo, 0u, 0u);

            var opt = new PowerPeripheralOptimizer(backend, tempBackup);

            // 1. Apply CPU Max Frequency Cap (2600 MHz): must clear Best Performance overlay & lower ThrottleMin to 5%
            Assert.True(opt.SetCpuMaxFreqMhz(2600));
            Assert.Equal(Guid.Empty, backend.CurrentOverlay);
            Assert.Equal((2600u, 2600u), backend.Read(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingProcFreqMax));
            Assert.Equal((5u, 5u), backend.Read(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingProcThrottleMin));

            // 2. Also enable Steady & Eco on top of 2600 MHz cap: must NOT lose bestPerfOverlay backup and MUST NOT touch NVMe/Wi-Fi/CorePark
            bool applied = opt.Apply(new SteadyEcoPowerOptions(
                EnableCpuCoreEco: true,
                EnablePeripheralEco: true,
                CpuEppIndex: 70,
                TopologyKind: CpuTopologyKind.SingleCcdHomogeneous,
                IsOnlineAntiCheatGame: false));

            Assert.True(applied);
            Assert.Equal(Guid.Empty, backend.CurrentOverlay);
            Assert.Equal((70u, 75u), backend.Read(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingPerfEpp0));
            Assert.Equal((100u, 100u), backend.Read(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingCoreParkMin));
            Assert.Equal((200u, 30u), backend.Read(PowerPeripheralOptimizer.SubDisk, PowerPeripheralOptimizer.SettingPrimaryNvmeIdleMs));
            Assert.Equal((2000u, 1000u), backend.Read(PowerPeripheralOptimizer.SubDisk, PowerPeripheralOptimizer.SettingSecondaryNvmeIdleMs));
            Assert.Equal((0u, 2u), backend.Read(PowerPeripheralOptimizer.SubWireless, PowerPeripheralOptimizer.SettingWirelessPowerSave));
            Assert.Equal((0u, 0u), backend.Read(PowerPeripheralOptimizer.SubMultimedia, PowerPeripheralOptimizer.SettingVideoQualityBias));

            // 3. Disable Steady & Eco while 2600 MHz cap remains active: EPP & Media restore, but Overlay=Empty & 2600 MHz stay active!
            opt.Restore();
            Assert.Equal((25u, 90u), backend.Read(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingPerfEpp0));
            Assert.Equal((1u, 0u), backend.Read(PowerPeripheralOptimizer.SubMultimedia, PowerPeripheralOptimizer.SettingVideoQualityBias));
            Assert.Equal(Guid.Empty, backend.CurrentOverlay);
            Assert.Equal((2600u, 2600u), backend.Read(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingProcFreqMax));
            Assert.Equal((5u, 5u), backend.Read(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingProcThrottleMin));

            // 4. Remove CPU cap (0): everything including Best Performance overlay & ThrottleMin=80 restores cleanly
            opt.SetCpuMaxFreqMhz(0);
            Assert.False(File.Exists(tempBackup));
            Assert.Equal(bestPerfOverlay, backend.CurrentOverlay);
            Assert.Equal((0u, 0u), backend.Read(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingProcFreqMax));
            Assert.Equal((80u, 5u), backend.Read(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingProcThrottleMin));

            // 5. Simulate sudden power loss during Steady & Eco: fresh instance on next boot recovers both values and Win11 Overlay
            opt.Apply(new SteadyEcoPowerOptions(
                EnableCpuCoreEco: true,
                EnablePeripheralEco: true,
                CpuEppIndex: 70,
                TopologyKind: CpuTopologyKind.SingleCcdHomogeneous,
                IsOnlineAntiCheatGame: false));
            Assert.Equal(Guid.Empty, backend.CurrentOverlay);

            var nextBootOpt = new PowerPeripheralOptimizer(backend, tempBackup);
            Assert.True(nextBootOpt.RecoverIfStaleBackupExists());
            Assert.False(File.Exists(tempBackup));
            Assert.Equal(bestPerfOverlay, backend.CurrentOverlay);
            Assert.Equal((25u, 90u), backend.Read(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingPerfEpp0));
            Assert.Equal((80u, 5u), backend.Read(PowerPeripheralOptimizer.SubProcessor, PowerPeripheralOptimizer.SettingProcThrottleMin));
        }
        finally
        {
            if (File.Exists(tempBackup)) File.Delete(tempBackup);
        }
    }

    private sealed class FakePowerBackend : IPowerSchemeBackend
    {
        public Guid ActiveScheme { get; } = new("381b4222-f694-41f0-96c1-ff5bb260df2e");
        public Guid CurrentOverlay { get; set; } = Guid.Empty;
        private readonly Dictionary<(Guid, Guid), (uint Ac, uint Dc)> _store = new();

        public void Seed(Guid sub, Guid setting, uint ac, uint dc) => _store[(sub, setting)] = (ac, dc);
        public (uint Ac, uint Dc) Read(Guid sub, Guid setting) => _store[(sub, setting)];

        public Guid GetActiveScheme() => ActiveScheme;

        public bool TryReadAcDcValues(Guid scheme, Guid subGroup, Guid setting, out uint acValue, out uint dcValue)
        {
            if (_store.TryGetValue((subGroup, setting), out var pair))
            {
                acValue = pair.Ac;
                dcValue = pair.Dc;
                return true;
            }
            acValue = 0;
            dcValue = 0;
            return false;
        }

        public bool TryWriteAcDcValues(Guid scheme, Guid subGroup, Guid setting, uint acValue, uint dcValue)
        {
            if (!_store.ContainsKey((subGroup, setting))) return false;
            _store[(subGroup, setting)] = (acValue, dcValue);
            return true;
        }

        public bool ActivateScheme(Guid scheme) => scheme == ActiveScheme;

        public bool TryGetOverlayScheme(out Guid overlayScheme)
        {
            overlayScheme = CurrentOverlay;
            return true;
        }

        public bool TrySetOverlayScheme(Guid overlayScheme)
        {
            CurrentOverlay = overlayScheme;
            return true;
        }
    }

    [Fact]
    public void AppSettings_ReconstructedUiLayoutThemeAndPerGameCpuCap_RoundTripsCleanly()
    {
        var settings = new AppSettings();
        Assert.Equal("aurora", settings.UiTheme);
        Assert.Equal("bento", settings.UiLayout);
        Assert.False(settings.FlatPowerLock);
        Assert.Equal(new[] { "BACK", "LB" }, settings.ConsoleGamepadCombo.Buttons);
        Assert.Empty(settings.MouseModeGamepadCombo.Buttons);
        Assert.Contains(10, settings.CustomTdpPresets);
        Assert.Contains(25, settings.CustomTdpPresets);

        settings.UiTheme = "frost";
        settings.UiLayout = "cockpit";
        settings.FlatPowerLock = true;
        settings.PerGameProfiles["Nioh3"] = new GamePerformanceProfile
        {
            GameId = "Nioh3",
            GameName = "仁王 3",
            Enabled = true,
            TdpWatts = 25,
            RefreshRateHz = -1,
            CpuMaxFreqMhz = 3000
        };

        string json = JsonSerializer.Serialize(settings);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal("frost", loaded.UiTheme);
        Assert.Equal("cockpit", loaded.UiLayout);
        Assert.True(loaded.FlatPowerLock);
        Assert.Equal(-1, loaded.PerGameProfiles["Nioh3"].RefreshRateHz);
        Assert.Equal(3000, loaded.PerGameProfiles["Nioh3"].CpuMaxFreqMhz);
    }
}
