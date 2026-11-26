using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using HaloDeck.Core.Services;
using Xunit;

namespace HaloDeck.Tests;

public sealed class MenuButtonAndUpdaterTests
{
    [Fact]
    public void GuideMenuTriggerGuard_FiresOnSoloShortPressAndIgnoresChordsLongPressAndDualF13Bounce()
    {
        var guard = new GuideMenuTriggerGuard(maxShortPressMs: 450, debounceMs: 350);

        // 1. Solo short press (120 ms) -> fires on release
        Assert.False(guard.UpdateGuideState(guideDown: true, anyOtherButtonOrTriggerActive: false, nowTickMs: 1000));
        Assert.True(guard.UpdateGuideState(guideDown: false, anyOtherButtonOrTriggerActive: false, nowTickMs: 1120));

        // 2. Immediate F13 arrival 40 ms later (dual-channel firmware) -> suppressed by shared 350 ms debounce
        Assert.False(guard.TryClaimQamMenuToggle(1160));

        // 3. After debounce window -> F13 succeeds
        Assert.True(guard.TryClaimQamMenuToggle(1600));

        // 4. Steam Guide + X chord -> MUST NOT trigger QAM on release
        Assert.False(guard.UpdateGuideState(true, false, 2500));
        Assert.False(guard.UpdateGuideState(true, true, 2560));
        Assert.False(guard.UpdateGuideState(false, false, 2650));

        // 5. Long press (> 450 ms) -> MUST NOT trigger QAM on release
        Assert.False(guard.UpdateGuideState(true, false, 3500));
        Assert.False(guard.UpdateGuideState(false, false, 4100));
    }

    [Fact]
    public void GpdVendorHidMenuParser_DetectsWin5A5AndB4RisingEdgeAndIgnoresHoldAndConfigPackets()
    {
        var parser = new GpdVendorHidMenuParser();

        // Standard Win 5 vendor HID report: ReportID=0x01, Magic=0xA5, byte[8]=0x68 (F13 Menu)
        var pressA5 = new byte[64];
        pressA5[0] = 0x01;
        pressA5[1] = 0xA5;
        pressA5[8] = 0x68;
        Assert.True(parser.ProcessReport(pressA5));

        // Held repeat packet (prevByte[8] was already 0x68) must NOT re-trigger
        Assert.False(parser.ProcessReport(pressA5));

        // Release clears state, then 0xB4 magic variant triggers on next rising edge
        var release = new byte[64];
        release[0] = 0x01;
        release[1] = 0xA5;
        release[8] = 0x00;
        Assert.False(parser.ProcessReport(release));

        var pressB4 = new byte[64];
        pressB4[0] = 0x01;
        pressB4[1] = 0xB4;
        pressB4[8] = 0x68;
        Assert.True(parser.ProcessReport(pressB4));

        // Config readback / unrelated magic (e.g. 0x41 version or 0x21 init1) must be ignored
        parser.Reset();
        var cfgResp = new byte[64];
        cfgResp[0] = 0x01;
        cfgResp[1] = 0x41;
        cfgResp[8] = 0x68;
        Assert.False(parser.ProcessReport(cfgResp));

        // Short packet (<9 bytes) must be ignored safely
        Assert.False(parser.ProcessReport(new byte[] { 0x01, 0xA5, 0x00 }));
    }

    [Fact]
    public void RawKeyboardMenuMatcher_MatchesF13KeyDownAndScanCode64Only()
    {
        Assert.True(RawKeyboardMenuMatcher.IsF13KeyDown(0x7C, 0x64, flags: 0));
        Assert.True(RawKeyboardMenuMatcher.IsF13KeyDown(0xFF, 0x64, flags: 0)); // unmapped VK with F13 scancode
        Assert.False(RawKeyboardMenuMatcher.IsF13KeyDown(0x7C, 0x64, flags: RawKeyboardMenuMatcher.RiKeyBreak)); // key-up
        Assert.False(RawKeyboardMenuMatcher.IsF13KeyDown(0x7D, 0x65, flags: 0)); // F14 must not match
    }

    [Fact]
    public void KeyCaptureDiagnosticSession_RecordsMultiChannelSignalsAndFormatsCopyableReport()
    {
        var session = new KeyCaptureDiagnosticSession(startTickMs: 10_000);
        session.Record(10_420, "GpdVendorHid", "magic=0xA5 key[8]=0x68", "01-A5-00-00-00-00-00-00-68", wouldTriggerQam: true);
        session.Record(10_435, "RawInputKbd", "vk=0x7C make=0x64", "7C-64", wouldTriggerQam: true);

        var env = new KeyCaptureEnvironmentInfo(
            SelfElevated: false,
            RegisterHotKeyF13Ok: false,
            RegisterHotKeyF13Error: 1409,
            GpdVendorHidConnected: true,
            GpdVendorHidDevice: "2F24:0137 (UP=0xFF00)",
            GpdVendorHidError: "",
            XInputBackend: "xinput1_4.dll (#100 Ex)",
            ConnectedXInputCount: 1,
            GpdToolRunning: true,
            GpdToolElevated: true,
            GpdToolServiceRunning: false,
            FirmwareX: "3.06",
            FirmwareK: "1.08",
            InterceptPhysicalMenuButton: true,
            InterceptGuideMenuButton: true);

        var report = session.BuildReport(env, durationMs: 5000);
        Assert.Equal(2, report.Events.Count);
        Assert.Equal(420, report.Events[0].OffsetMs);
        Assert.Contains("GpdVendorHid", report.CopyableReport);
        Assert.Contains("2F24:0137", report.CopyableReport);
        Assert.Contains("Win32=1409", report.CopyableReport);
        Assert.Contains("[可触发侧栏]", report.CopyableReport);
    }

    [Fact]
    public void GuideMenuTriggerGuard_DeduplicatesMultiChannelSimultaneousMenuSignals()
    {
        var guard = new GuideMenuTriggerGuard(maxShortPressMs: 450, debounceMs: 350);

        // Vendor HID + RawInput + LLHook + AsyncKeyState arrive within 30ms for a single button press
        Assert.True(guard.TryClaimQamMenuToggle(2000));
        Assert.False(guard.TryClaimQamMenuToggle(2008));
        Assert.False(guard.TryClaimQamMenuToggle(2016));
        Assert.False(guard.TryClaimQamMenuToggle(2030));

        // Next intentional press after debounce window succeeds
        Assert.True(guard.TryClaimQamMenuToggle(2400));
    }

    [Fact]
    public void AppUpdateService_CompareVersions_HandlesStablePrereleaseAndPrefixes()
    {
        Assert.True(AppUpdateService.CompareVersions("v0.6.2", "0.6.1") > 0);
        Assert.True(AppUpdateService.CompareVersions("0.6.2", "0.6.2-rc.1") > 0);
        Assert.True(AppUpdateService.CompareVersions("0.6.2-rc.2", "0.6.2-rc.1") > 0);
        Assert.True(AppUpdateService.CompareVersions("0.6.2-rc.1", "0.6.1") > 0);
        Assert.Equal(0, AppUpdateService.CompareVersions("v0.6.2+abc123", "0.6.2"));
        Assert.True(AppUpdateService.CompareVersions("0.6.1", "0.6.2") < 0);
    }

    [Fact]
    public async Task AppUpdateService_CheckForUpdateAsync_ParsesStableAndSkipsPluginStoreTagInPrereleaseMode()
    {
        string latestJson = """
            {
              "tag_name": "v0.6.2",
              "name": "HaloDeck v0.6.2",
              "body": "Release notes body",
              "html_url": "https://github.com/Lokilankaaa/HaloDeck/releases/tag/v0.6.2",
              "prerelease": false,
              "published_at": "2026-11-25T12:00:00Z",
              "assets": [
                { "name": "HaloDeck-win-x64-portable.zip", "size": 74000000, "browser_download_url": "https://github.com/Lokilankaaa/HaloDeck/releases/download/v0.6.2/HaloDeck-win-x64-portable.zip" },
                { "name": "HaloDeck-win-x64-portable.zip.sha256", "size": 97, "browser_download_url": "https://github.com/Lokilankaaa/HaloDeck/releases/download/v0.6.2/HaloDeck-win-x64-portable.zip.sha256" }
              ]
            }
            """;

        string listJson = """
            [
              { "tag_name": "plugin-store-builds", "draft": false, "prerelease": false, "assets": [] },
              {
                "tag_name": "v0.6.2-rc.1",
                "name": "HaloDeck v0.6.2-rc.1",
                "body": "RC hotfix",
                "html_url": "https://github.com/Lokilankaaa/HaloDeck/releases/tag/v0.6.2-rc.1",
                "prerelease": true,
                "assets": [
                  { "name": "HaloDeck-win-x64-portable.zip", "size": 74000000, "browser_download_url": "https://github.com/Lokilankaaa/HaloDeck/releases/download/v0.6.2-rc.1/HaloDeck-win-x64-portable.zip" }
                ]
              }
            ]
            """;

        var handler = new StubHandler(req =>
        {
            string u = req.RequestUri!.AbsoluteUri;
            if (u.Contains("releases.atom") || u.Contains("github.com/Lokilankaaa/HaloDeck/releases/latest"))
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            string payload = u.Contains("/releases/latest") ? latestJson : listJson;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        });

        using var http = new HttpClient(handler);
        using var svc = new AppUpdateService(http);

        var stable = await svc.CheckForUpdateAsync("0.6.1", includePrerelease: false);
        Assert.True(stable.HasUpdate);
        Assert.Equal("0.6.2", stable.LatestVersion);
        Assert.False(stable.IsPrerelease);
        Assert.EndsWith("HaloDeck-win-x64-portable.zip", stable.DownloadUrl);
        Assert.EndsWith(".sha256", stable.Sha256Url);

        var rc = await svc.CheckForUpdateAsync("0.6.1", includePrerelease: true);
        Assert.True(rc.HasUpdate);
        Assert.Equal("0.6.2-rc.1", rc.LatestVersion);
        Assert.True(rc.IsPrerelease);
    }

    [Fact]
    public async Task AppUpdateService_PrimaryAtomFeedSucceedsEvenWhenGitHubApiReturns403()
    {
        string atomXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <id>tag:github.com,2008:Repository/1/plugin-store-builds</id>
                <link rel="alternate" href="https://github.com/Lokilankaaa/HaloDeck/releases/tag/plugin-store-builds"/>
                <title>Store Builds</title>
              </entry>
              <entry>
                <id>tag:github.com,2008:Repository/1/v0.6.2-rc.1</id>
                <link rel="alternate" href="https://github.com/Lokilankaaa/HaloDeck/releases/tag/v0.6.2-rc.1"/>
                <title>HaloDeck v0.6.2-rc.1 (预发布测试版)</title>
                <updated>2026-11-25T14:46:29Z</updated>
                <content type="html">&lt;p&gt;Dual-channel menu button + self-updater&lt;/p&gt;</content>
              </entry>
              <entry>
                <id>tag:github.com,2008:Repository/1/v0.6.1</id>
                <link rel="alternate" href="https://github.com/Lokilankaaa/HaloDeck/releases/tag/v0.6.1"/>
                <title>HaloDeck v0.6.1</title>
                <updated>2026-11-24T17:12:56Z</updated>
                <content type="html">&lt;h2&gt;v0.6.1&lt;/h2&gt;&lt;p&gt;Stable notes&lt;/p&gt;</content>
              </entry>
            </feed>
            """;

        var handler = new StubHandler(req =>
        {
            string u = req.RequestUri!.AbsoluteUri;
            if (u.Contains("api.github.com"))
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden); // Simulate real-world 403 rate limit!
            }
            if (u.Contains("releases.atom"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(atomXml, Encoding.UTF8, "application/atom+xml")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler);
        using var svc = new AppUpdateService(http);

        var stable = await svc.CheckForUpdateAsync("0.6.0", includePrerelease: false);
        Assert.True(stable.HasUpdate);
        Assert.Equal("0.6.1", stable.LatestVersion);
        Assert.False(stable.IsPrerelease);
        Assert.Contains("Stable notes", stable.ReleaseNotes);
        Assert.EndsWith("/v0.6.1/HaloDeck-win-x64-portable.zip", stable.DownloadUrl);

        var rc = await svc.CheckForUpdateAsync("0.6.1", includePrerelease: true);
        Assert.True(rc.HasUpdate);
        Assert.Equal("0.6.2-rc.1", rc.LatestVersion);
        Assert.True(rc.IsPrerelease);
        Assert.Contains("Dual-channel", rc.ReleaseNotes);
    }

    [Fact]
    public async Task AppUpdateService_DownloadAndStageUpdateAsync_RejectsTamperedMirrorFallsBackAndWritesUpdaterScript()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "HaloDeck_UpdTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string zipFile = Path.Combine(tempRoot, "portable.zip");
        StagedAppUpdate? staged = null;

        try
        {
            using (var za = ZipFile.Open(zipFile, ZipArchiveMode.Create))
            {
                var entry = za.CreateEntry("HaloDeck.exe");
                using var sw = new StreamWriter(entry.Open());
                sw.Write("FAKE-EXE-PAYLOAD");
            }
            byte[] goodZip = await File.ReadAllBytesAsync(zipFile);
            string goodSha = Convert.ToHexString(SHA256.HashData(goodZip)).ToLowerInvariant();

            string directZipUrl = "https://github.com/Lokilankaaa/HaloDeck/releases/download/v0.6.2/HaloDeck-win-x64-portable.zip";
            string directShaUrl = directZipUrl + ".sha256";
            string mirror = "https://mirror.example.test/";

            var seenUrls = new List<string>();
            var handler = new StubHandler(req =>
            {
                string u = req.RequestUri!.AbsoluteUri;
                seenUrls.Add(u);
                if (u.EndsWith(".sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent($"{goodSha}  HaloDeck-win-x64-portable.zip")
                    };
                }
                if (u == mirror + directZipUrl)
                {
                    // Tampered mirror response -> SHA-256 must reject and fall back to direct
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(Encoding.UTF8.GetBytes("corrupted-mirror-zip"))
                    };
                }
                if (u == directZipUrl)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(goodZip)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            using var http = new HttpClient(handler);
            using var svc = new AppUpdateService(http);

            var info = new AppUpdateInfo
            {
                LatestVersion = "0.6.2",
                HasUpdate = true,
                DownloadUrl = directZipUrl,
                Sha256Url = directShaUrl
            };

            staged = await svc.DownloadAndStageUpdateAsync(info, mirrorPrefix: mirror);
            Assert.Equal(goodSha, staged.VerifiedSha256);
            Assert.True(File.Exists(Path.Combine(staged.PayloadDir, "HaloDeck.exe")));
            Assert.Contains(mirror + directZipUrl, seenUrls);
            Assert.Contains(directZipUrl, seenUrls);

            string scriptPath = AppUpdateService.WritePortableUpdaterScript(staged, tempRoot, currentPid: 999999);
            Assert.True(File.Exists(scriptPath));
            string scriptText = await File.ReadAllTextAsync(scriptPath);
            Assert.Contains("HaloDeck.exe", scriptText);
            Assert.Contains("999999", scriptText);
        }
        finally
        {
            if (staged is not null && Directory.Exists(staged.StageRoot))
            {
                try { Directory.Delete(staged.StageRoot, true); } catch { }
            }
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(fn(request));
    }
}
