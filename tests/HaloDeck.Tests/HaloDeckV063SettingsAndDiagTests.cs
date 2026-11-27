using HaloDeck.Core.Components;
using HaloDeck.Core.Config;
using HaloDeck.Core.Models;
using HaloDeck.Core.Plugins;
using HaloDeck.Core.Services;
using Xunit;

namespace HaloDeck.Tests;

public sealed class HaloDeckV063SettingsAndDiagTests
{
    [Fact]
    public void DiagnosticReportBuilder_SanitizePaths_MasksAsciiChineseAndForwardSlashUsernames()
    {
        string raw = @"Logs at C:\Users\AliceSmith\AppData\Local\HaloDeck\logs\last-update.log and D:\Users\测试玩家\Games and C:/Users/Bob 123/file.txt";
        string sanitized = DiagnosticReportBuilder.SanitizePaths(raw);

        Assert.DoesNotContain("AliceSmith", sanitized);
        Assert.DoesNotContain("测试玩家", sanitized);
        Assert.DoesNotContain("Bob 123", sanitized);
        Assert.Contains(@"C:\Users\***\AppData\Local\HaloDeck\logs\last-update.log", sanitized);
        Assert.Contains(@"D:\Users\***\Games", sanitized);
        Assert.Contains(@"C:/Users/***/file.txt", sanitized);
    }

    [Fact]
    public void DiagnosticReportBuilder_Build_IncludesAllSectionsAndMasksPaths()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"HaloDeck_DiagTest_{Guid.NewGuid():N}");
        try
        {
            string logPath = HaloDeckPaths.ResolveUpdateLogPath(tempRoot);
            File.WriteAllText(logPath, "[2026-11-27 10:00:00] Updated C:\\Users\\SecretPlayer\\HaloDeck\\HaloDeck.exe ok\n");

            string tail = DiagnosticReportBuilder.ReadUpdateLogTail(tempRoot);
            var input = new DiagnosticReportInput
            {
                AppVersion = "0.6.3",
                IsElevated = false,
                OsVersion = "Microsoft Windows NT 10.0.26100.0",
                CpuName = "AMD Ryzen AI MAX+ 395",
                DedicatedVramMb = 16384,
                TotalRamGb = 64,
                ScreenWidth = 1920,
                ScreenHeight = 1080,
                RefreshRateHz = 120,
                IsDynamicRefreshRate = true,
                HardwareServiceRunning = true,
                HardwareDaemonVersion = 5,
                SmuReady = true,
                EcReady = true,
                CurrentTdpWatts = 40,
                HwStapmLimitWatts = 40,
                FanMode = "curve",
                FanRpm = 3180,
                GpdConnected = true,
                GpdFirmwareX = "405",
                GpdFirmwareK = "109",
                InterceptPhysicalMenuButton = true,
                InterceptGuideMenuButton = true,
                ActiveOptiScalerSlot = nameof(ComponentSlot.OptiScalerNightly),
                DownloadMirrorPrefix = "https://gh-proxy.com/",
                UserDataDirectory = @"C:\Users\SecretPlayer\AppData\Local\HaloDeck",
                LogsDirectory = @"C:\Users\SecretPlayer\AppData\Local\HaloDeck\logs",
                Components = new[]
                {
                    new DiagnosticComponentEntry { Name = "OptiScaler (Nightly)", Installed = true, Version = "v0.9.9-pre10" },
                    new DiagnosticComponentEntry { Name = "MFG Unlocker", Installed = false, Version = "" }
                },
                Plugins = new[]
                {
                    new DiagnosticPluginEntry { Id = "halodeck.theme-studio", Name = "Theme Studio", Version = "1.2.0", Enabled = true, IsValid = true }
                },
                UpdateLogTail = tail
            };

            string report = DiagnosticReportBuilder.Build(input);

            Assert.Contains("HaloDeck v0.6.3", report);
            Assert.Contains("标准用户权限", report);
            Assert.Contains("16384 MB", report);
            Assert.Contains("协议 v5", report);
            Assert.Contains("PawnIO/SMU=就绪", report);
            Assert.Contains("FW X405 / K109", report);
            Assert.Contains("OptiScaler (Nightly)(v0.9.9-pre10)", report);
            Assert.Contains("halodeck.theme-studio@1.2.0", report);
            Assert.Contains("https://gh-proxy.com/", report);
            Assert.Contains("last-update.log", report);
            Assert.Contains(@"C:\Users\***\AppData\Local\HaloDeck", report);
            Assert.DoesNotContain("SecretPlayer", report);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("direct", "")]
    [InlineData("ghproxy", "https://gh-proxy.com/")]
    [InlineData("ghfast", "https://ghfast.top/")]
    [InlineData("https://mirror.example.com", "https://mirror.example.com/")]
    [InlineData("https://mirror.example.com/", "https://mirror.example.com/")]
    public void PluginRegistryClient_NormalizeMirrorPrefix_AcceptsPresetsAndHttps(string input, string expected)
    {
        Assert.Equal(expected, PluginRegistryClient.NormalizeMirrorPrefix(input));
    }

    [Theory]
    [InlineData("http://insecure.example.com/")]
    [InlineData("file:///C:/Windows/System32")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not-a-valid-url")]
    public void PluginRegistryClient_NormalizeMirrorPrefix_RejectsUnsafeSchemes(string badInput)
    {
        Assert.Throws<ArgumentException>(() => PluginRegistryClient.NormalizeMirrorPrefix(badInput));
    }

    [Fact]
    public void HaloDeckPaths_UpdateLogPathAndUpdaterScript_UseLogsSubdirectory()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"HaloDeck_LogPathTest_{Guid.NewGuid():N}");
        string stageRoot = Path.Combine(tempRoot, "stage");
        string payloadDir = Path.Combine(stageRoot, "payload");
        Directory.CreateDirectory(payloadDir);

        try
        {
            string resolvedLog = HaloDeckPaths.ResolveUpdateLogPath(tempRoot);
            Assert.EndsWith(Path.Combine("logs", "last-update.log"), resolvedLog);
            Assert.True(Directory.Exists(Path.GetDirectoryName(resolvedLog)!));

            var staged = new StagedAppUpdate { StageRoot = stageRoot, PayloadDir = payloadDir, Version = "0.6.3" };
            string scriptPath = AppUpdateService.WritePortableUpdaterScript(staged, tempRoot, currentPid: 999999, launchAfterApply: false);
            string scriptText = File.ReadAllText(scriptPath);
            Assert.Contains("'logs'", scriptText);
            Assert.Contains("'last-update.log'", scriptText);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public void ComponentManager_DefaultCatalog_PopulatesLicenseAuthorAndHomepage()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"HaloDeck_CompMeta_{Guid.NewGuid():N}");
        try
        {
            var mgr = new ComponentManager(tempRoot);
            Assert.True(Directory.Exists(mgr.ComponentsRoot));
            var comps = mgr.GetComponents();
            Assert.All(comps, c =>
            {
                Assert.Equal("MIT", c.License);
                Assert.False(string.IsNullOrWhiteSpace(c.Author));
                Assert.StartsWith("https://github.com/", c.HomepageUrl);
            });
            var mfg = comps.Single(c => c.Slot == ComponentSlot.MfgUnlocker);
            Assert.Equal("dashdogy", mfg.Author);
            var fake = comps.Single(c => c.Slot == ComponentSlot.Fakenvapi);
            Assert.Equal("FakeMichau", fake.Author);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }
}
