using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HaloDeck.Core.Plugins;
using Xunit;

namespace HaloDeck.Tests;

public sealed class HaloDeckV062CommunityTests
{
    [Fact]
    public void PluginManifest_AllowsShellOpenUrlPermission()
    {
        var manifest = new PluginManifest
        {
            SchemaVersion = 1,
            Id = "community.wiki-helper",
            Name = "Wiki Helper",
            Version = "1.0.0",
            Entry = "ui/index.html",
            Permissions = new List<string> { "games:read", "shell:openUrl" }
        };

        var (valid, err) = PluginManifest.Validate(manifest);
        Assert.True(valid, err);
        Assert.True(manifest.HasPermission("shell:openUrl"));
    }

    [Fact]
    public void BuildDownloadCandidateUrls_AppliesHttpsMirrorForGitHubAndFallsBackDirect()
    {
        string ghUrl = "https://github.com/Lokilankaaa/HaloDeck/releases/download/v0.6.1/halodeck.skin-studio.zip";

        var withMirror = PluginRegistryClient.BuildDownloadCandidateUrls(ghUrl, "https://gh-proxy.com");
        Assert.Equal(2, withMirror.Count);
        Assert.Equal("https://gh-proxy.com/" + ghUrl, withMirror[0]);
        Assert.Equal(ghUrl, withMirror[1]);

        // Trailing slash normalized without double slash
        var withSlash = PluginRegistryClient.BuildDownloadCandidateUrls(ghUrl, "https://gh-proxy.com/");
        Assert.Equal("https://gh-proxy.com/" + ghUrl, withSlash[0]);

        // Rejects insecure http:// mirror prefix
        var insecureMirror = PluginRegistryClient.BuildDownloadCandidateUrls(ghUrl, "http://evil.local/");
        Assert.Single(insecureMirror);
        Assert.Equal(ghUrl, insecureMirror[0]);

        // Does not rewrite non-GitHub URLs
        string otherUrl = "https://example.com/plugins/demo.zip";
        var nonGh = PluginRegistryClient.BuildDownloadCandidateUrls(otherUrl, "https://gh-proxy.com/");
        Assert.Single(nonGh);
        Assert.Equal(otherUrl, nonGh[0]);
    }

    [Fact]
    public void InstallFromZip_CallsBeforeOverwriteBeforeDeletingLockedFile()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "HaloDeck_BeforeOverwrite_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string zipV1 = Path.Combine(tempRoot, "v1.zip");
        string zipV2 = Path.Combine(tempRoot, "v2.zip");
        FileStream? heldLock = null;

        try
        {
            CreateTestPluginZip(zipV1, "test.lock", "1.0.0", new[] { "storage" });
            CreateTestPluginZip(zipV2, "test.lock", "1.1.0", new[] { "storage" });

            var store = new PluginStoreService(Path.Combine(tempRoot, "appdata"));
            var v1 = store.InstallFromZip(zipV1);
            store.SetEnabled(v1.Id, true, consentAccepted: true);

            // Simulate a running backend holding an open exclusive file lock inside the installed plugin folder
            string lockedFile = Path.Combine(v1.RootDirectory, "ui", "index.html");
            heldLock = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None);

            bool callbackFired = false;
            var v2 = store.InstallFromZip(zipV2, beforeOverwrite: id =>
            {
                Assert.Equal("test.lock", id);
                callbackFired = true;
                heldLock?.Dispose();
                heldLock = null;
            });

            Assert.True(callbackFired);
            Assert.Equal("1.1.0", v2.Version);
            Assert.True(v2.Enabled);
            Assert.True(v2.ConsentAccepted);
        }
        finally
        {
            heldLock?.Dispose();
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public void InstallFromDirectory_SyncsUnpackedPluginAndEnforcesPermissionEscalation()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "HaloDeck_DevDir_" + Guid.NewGuid().ToString("N"));
        string srcDir = Path.Combine(tempRoot, "dev-src");
        Directory.CreateDirectory(Path.Combine(srcDir, "ui"));

        try
        {
            File.WriteAllText(Path.Combine(srcDir, "plugin.json"), """
                { "schemaVersion": 1, "id": "dev.live", "name": "Dev Live", "version": "0.1.0", "entry": "ui/index.html", "permissions": ["storage"] }
                """);
            File.WriteAllText(Path.Combine(srcDir, "ui", "index.html"), "<h1>v0.1</h1>");

            var store = new PluginStoreService(Path.Combine(tempRoot, "appdata"));
            var p1 = store.InstallFromDirectory(srcDir);
            store.SetEnabled(p1.Id, true, consentAccepted: true);

            // Same permissions update -> stays enabled
            File.WriteAllText(Path.Combine(srcDir, "plugin.json"), """
                { "schemaVersion": 1, "id": "dev.live", "name": "Dev Live", "version": "0.2.0", "entry": "ui/index.html", "permissions": ["storage"] }
                """);
            File.WriteAllText(Path.Combine(srcDir, "ui", "index.html"), "<h1>v0.2</h1>");
            var p2 = store.InstallFromDirectory(srcDir);
            Assert.Equal("0.2.0", p2.Version);
            Assert.True(p2.Enabled);
            Assert.Contains("v0.2", File.ReadAllText(Path.Combine(p2.RootDirectory, "ui", "index.html")));

            // Escalating permissions -> revokes consent & disables
            File.WriteAllText(Path.Combine(srcDir, "plugin.json"), """
                { "schemaVersion": 1, "id": "dev.live", "name": "Dev Live", "version": "0.3.0", "entry": "ui/index.html", "permissions": ["storage", "shell:openUrl"] }
                """);
            var p3 = store.InstallFromDirectory(srcDir);
            Assert.False(p3.Enabled);
            Assert.False(p3.ConsentAccepted);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public async Task DownloadAndInstallAsync_FallsBackFromTamperedOrFailingMirrorToDirectAndVerifiesSha256()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "HaloDeck_MirrorFb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string goodZipPath = Path.Combine(tempRoot, "good.zip");
        try
        {
            CreateTestPluginZip(goodZipPath, "test.mirror", "1.2.0", new[] { "storage" });
            byte[] goodBytes = await File.ReadAllBytesAsync(goodZipPath);
            string goodSha = Convert.ToHexString(SHA256.HashData(goodBytes)).ToLowerInvariant();
            byte[] tamperedBytes = Encoding.UTF8.GetBytes("tampered-payload-not-a-valid-zip");

            string directUrl = "https://github.com/Lokilankaaa/HaloDeck/releases/download/v0.6.1/test.mirror.zip";
            string mirrorPrefix = "https://mirror.example.test/";
            string mirroredUrl = mirrorPrefix + directUrl;

            var requestedUrls = new List<string>();
            var handler = new StubHttpHandler(req =>
            {
                string u = req.RequestUri!.AbsoluteUri;
                requestedUrls.Add(u);
                if (u == mirroredUrl)
                {
                    // Mirror serves tampered bytes -> SHA-256 check MUST reject and fall back to direct!
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(tamperedBytes)
                    };
                }
                if (u == directUrl)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(goodBytes)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            using var http = new HttpClient(handler);
            using var registry = new PluginRegistryClient(http);
            var store = new PluginStoreService(Path.Combine(tempRoot, "appdata"));

            string? stoppedId = null;
            var installed = await registry.DownloadAndInstallAsync(
                store,
                directUrl,
                expectedSha256: goodSha,
                mirrorPrefix: mirrorPrefix,
                beforeOverwrite: id => stoppedId = id);

            Assert.Equal(2, requestedUrls.Count);
            Assert.Equal(mirroredUrl, requestedUrls[0]);
            Assert.Equal(directUrl, requestedUrls[1]);
            Assert.Equal("test.mirror", installed.Id);
            Assert.Equal("1.2.0", installed.Version);
            Assert.Equal("test.mirror", stoppedId);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public void OfficialStoreJson_AllEntriesHavePinnedSha256AndImmutableCommit()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string storePath = Path.Combine(repoRoot, "plugins", "store.json");
        Assert.True(File.Exists(storePath));

        var catalog = JsonSerializer.Deserialize<PluginStoreCatalog>(
            File.ReadAllText(storePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(catalog);
        Assert.True(catalog!.Plugins.Count >= 3);

        foreach (var item in catalog.Plugins)
        {
            Assert.Matches(PluginManifest.ValidIdRegex, item.Id);
            Assert.True(PluginRegistryClient.IsAllowedHttpsUrl(item.ZipUrl));
            Assert.DoesNotContain("/releases/latest/", item.ZipUrl, StringComparison.OrdinalIgnoreCase);
            Assert.Matches("^[a-fA-F0-9]{64}$", item.Sha256);
            Assert.Matches("^[a-fA-F0-9]{40}$", item.SourceCommit);
        }
    }

    [Fact]
    public async Task BackendDemoWorkerPs1_HandlesStdioJsonRpcPingAndExportReportEndToEnd()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string workerPath = Path.Combine(repoRoot, "examples", "plugins", "halodeck.backend-demo", "backend", "worker.ps1");
        Assert.True(File.Exists(workerPath));

        string dataDir = Path.Combine(Path.GetTempPath(), "HaloDeck_WorkerE2E_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(workerPath);
        psi.Environment["HALODECK_PLUGIN_ID"] = "halodeck.backend-demo";
        psi.Environment["HALODECK_PLUGIN_DATA_DIR"] = dataDir;

        using var proc = System.Diagnostics.Process.Start(psi);
        Assert.NotNull(proc);
        try
        {
            await proc!.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":"1","method":"ping","params":{}}""");
            await proc.StandardInput.FlushAsync();
            string? pingLine = await proc.StandardOutput.ReadLineAsync();
            Assert.NotNull(pingLine);
            using (var pingDoc = JsonDocument.Parse(pingLine!))
            {
                Assert.Equal("1", pingDoc.RootElement.GetProperty("id").GetString());
                Assert.True(pingDoc.RootElement.GetProperty("result").GetProperty("pong").GetBoolean());
            }

            await proc.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":"2","method":"exportDiagnosticReport","params":{"gameCount":9,"telemetry":{"apuPowerWatts":32.5,"cpuTempC":60,"gpuTempC":56,"batteryPercent":88,"isAcOnline":false,"screenWidth":1920,"screenHeight":1080,"refreshRateHz":120}}}""");
            await proc.StandardInput.FlushAsync();

            int progressEvents = 0;
            string? fileName = null;
            while (true)
            {
                string? line = await proc.StandardOutput.ReadLineAsync();
                Assert.NotNull(line);
                using var doc = JsonDocument.Parse(line!);
                if (doc.RootElement.TryGetProperty("method", out var mp) && mp.GetString() == "progress")
                {
                    progressEvents++;
                    continue;
                }
                if (doc.RootElement.TryGetProperty("id", out var idp) && idp.GetString() == "2")
                {
                    var res = doc.RootElement.GetProperty("result");
                    Assert.True(res.GetProperty("ok").GetBoolean());
                    fileName = res.GetProperty("fileName").GetString();
                    break;
                }
            }

            Assert.True(progressEvents >= 2);
            Assert.False(string.IsNullOrWhiteSpace(fileName));
            string reportPath = Path.Combine(dataDir, fileName!);
            Assert.True(File.Exists(reportPath));
            string reportContent = await File.ReadAllTextAsync(reportPath);
            Assert.Contains("32.5 W", reportContent);
            Assert.Contains("9", reportContent);
        }
        finally
        {
            try { proc?.StandardInput.Close(); } catch { }
            if (proc is not null && !proc.WaitForExit(3000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
            }
            try { Directory.Delete(dataDir, true); } catch { }
        }
    }

    private static void CreateTestPluginZip(string zipPath, string id, string version, string[] perms)
    {
        using var za = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        string permsJson = string.Join(",", perms.Select(p => $"\"{p}\""));
        WriteZipEntry(za, $"{id}/plugin.json",
            $"{{ \"schemaVersion\": 1, \"id\": \"{id}\", \"name\": \"{id}\", \"version\": \"{version}\", \"entry\": \"ui/index.html\", \"permissions\": [{permsJson}] }}");
        WriteZipEntry(za, $"{id}/ui/index.html", $"<h1>{id} {version}</h1>");
    }

    private static void WriteZipEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
