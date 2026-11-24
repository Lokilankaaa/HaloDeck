using System.IO.Compression;
using System.Text.Json;
using HaloDeck.Core.Plugins;
using Xunit;

namespace HaloDeck.Tests;

public sealed class HaloDeckV060PluginTests
{
    [Fact]
    public void PluginManifest_RejectsInvalidIdsTraversalAndUndeclaredBackendPermission()
    {
        var badId = new PluginManifest { Id = "../evil", Name = "Evil", Entry = "ui/index.html" };
        Assert.False(PluginManifest.Validate(badId).Valid);

        var badEntry = new PluginManifest { Id = "demo.test", Name = "Test", Entry = "../secret.html" };
        Assert.False(PluginManifest.Validate(badEntry).Valid);

        var unknownPerm = new PluginManifest
        {
            Id = "demo.test",
            Name = "Test",
            Entry = "ui/index.html",
            Permissions = new() { "pawnio:raw" }
        };
        Assert.False(PluginManifest.Validate(unknownPerm).Valid);

        var missingBackendPerm = new PluginManifest
        {
            Id = "demo.test",
            Name = "Test",
            Entry = "ui/index.html",
            Backend = new PluginBackendConfig { Type = "stdio-jsonrpc", Executable = "backend/worker.exe" },
            Permissions = new() { "games:read" }
        };
        Assert.False(PluginManifest.Validate(missingBackendPerm).Valid);

        var valid = new PluginManifest
        {
            Id = "demo.valid-plugin",
            Name = "Valid Plugin",
            Entry = "ui/index.html",
            Mount = new() { "console", "qam" },
            Activation = new() { "onView", "onGameLaunch" },
            Backend = new PluginBackendConfig { Type = "stdio-jsonrpc", Executable = "backend/worker.ps1" },
            Permissions = new() { "games:read", "events:game", "storage", "backend" }
        };
        var (ok, err) = PluginManifest.Validate(valid);
        Assert.True(ok, err);
    }

    [Fact]
    public void PluginStoreService_RejectsZipSlipArchiveAttack()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "HaloDeckPluginTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string zipPath = Path.Combine(tempRoot, "malicious.zip");
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var evilEntry = zip.CreateEntry("../../escaped.txt");
                using var w = new StreamWriter(evilEntry.Open());
                w.Write("nope");
            }

            var store = new PluginStoreService(Path.Combine(tempRoot, "appdata"));
            var ex = Assert.Throws<InvalidOperationException>(() => store.InstallFromZip(zipPath));
            Assert.Contains("Zip-Slip", ex.Message);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void PluginStoreService_InstallsWrapperZipEnforcesConsentAndIsolatesKvStorage()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "HaloDeckPluginTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string zipPath = Path.Combine(tempRoot, "sample.zip");
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(zip.CreateEntry("demo.notes/plugin.json").Open()))
                {
                    w.Write("""
                    {
                      "schemaVersion": 1,
                      "id": "demo.notes",
                      "name": "Demo Notes",
                      "version": "1.2.0",
                      "author": "Tester",
                      "mount": ["console", "qam"],
                      "entry": "ui/index.html",
                      "permissions": ["games:read", "storage"]
                    }
                    """);
                }
                using (var w = new StreamWriter(zip.CreateEntry("demo.notes/ui/index.html").Open()))
                {
                    w.Write("<!doctype html><html><body>OK</body></html>");
                }
            }

            var store = new PluginStoreService(Path.Combine(tempRoot, "appdata"));
            var installed = store.InstallFromZip(zipPath);
            Assert.Equal("demo.notes", installed.Id);
            Assert.True(installed.IsValid);
            Assert.False(installed.Enabled);
            Assert.Equal("https://plugins.halodeck.local/demo.notes/ui/index.html", installed.EntryUrl);

            // Enabling without consent must fail
            Assert.Throws<InvalidOperationException>(() => store.SetEnabled("demo.notes", enabled: true, consentAccepted: false));

            // Enabling with explicit user consent succeeds and persists
            var enabled = store.SetEnabled("demo.notes", enabled: true, consentAccepted: true);
            Assert.True(enabled.Enabled);
            Assert.True(enabled.ConsentAccepted);

            // Isolated KV storage round-trip
            using var valDoc = JsonDocument.Parse("{\"note\":\"Boss弱火\"}");
            store.StorageSet("demo.notes", "elden", valDoc.RootElement);
            var loaded = store.StorageGet("demo.notes", "elden");
            Assert.NotNull(loaded);
            Assert.Equal("Boss弱火", loaded!.Value.GetProperty("note").GetString());

            store.StorageRemove("demo.notes", "elden");
            Assert.Null(store.StorageGet("demo.notes", "elden"));

            // Uninstall preserves data when deleteData=false
            store.Uninstall("demo.notes", deleteData: false);
            Assert.Empty(store.ListPlugins());
            Assert.True(Directory.Exists(Path.Combine(store.PluginDataRoot, "demo.notes")));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void PluginStoreService_ThemeOverrideSanitizesCssAndAutoResetsOnDisable()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "HaloDeckThemePluginTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string zipPath = Path.Combine(tempRoot, "skin.zip");
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(zip.CreateEntry("demo.skin/plugin.json").Open()))
                {
                    w.Write("""
                    {
                      "schemaVersion": 1,
                      "id": "demo.skin",
                      "name": "Demo Skin",
                      "version": "1.0.0",
                      "mount": ["console", "qam"],
                      "entry": "ui/index.html",
                      "permissions": ["ui:theme"]
                    }
                    """);
                }
                using (var w = new StreamWriter(zip.CreateEntry("demo.skin/ui/index.html").Open()))
                {
                    w.Write("<!doctype html><html><body>Skin</body></html>");
                }
            }

            var store = new PluginStoreService(Path.Combine(tempRoot, "appdata"));
            store.InstallFromZip(zipPath, enableAfterInstall: true, consentAccepted: true);

            // Reject non-allowlisted CSS variable & unsafe values (url / semicolon)
            Assert.Throws<ArgumentException>(() => store.SetThemeOverride("demo.skin", new() { ["--evil-var"] = "#fff" }));
            Assert.Throws<ArgumentException>(() => store.SetThemeOverride("demo.skin", new() { ["--accent"] = "url(https://evil.com/x)" }));
            Assert.Throws<ArgumentException>(() => store.SetThemeOverride("demo.skin", new() { ["--accent"] = "#fff; background: red" }));

            // Valid theme variables apply and persist
            store.SetThemeOverride("demo.skin", new() { ["--accent"] = "#ec4899", ["--bg-app"] = "rgba(9, 7, 20, 1)" });
            var active = store.GetActiveThemeOverride();
            Assert.NotNull(active);
            Assert.Equal("#ec4899", active!.Variables["--accent"]);

            // Disabling the skin plugin immediately clears its active theme override
            store.SetEnabled("demo.skin", enabled: false, consentAccepted: true);
            Assert.Null(store.GetActiveThemeOverride());
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData("halodeck.game-notes")]
    [InlineData("halodeck.skin-studio")]
    [InlineData("halodeck.backend-demo")]
    public void BundledExamplePlugins_PassStrictManifestAndFileValidation(string folderName)
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string pluginDir = Path.Combine(repoRoot, "examples", "plugins", folderName);
        Assert.True(Directory.Exists(pluginDir), $"Expected example plugin directory at {pluginDir}");

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var manifest = JsonSerializer.Deserialize<PluginManifest>(
            File.ReadAllText(Path.Combine(pluginDir, "plugin.json")),
            opts);
        var (valid, err) = PluginManifest.Validate(manifest, pluginDir);
        Assert.True(valid, err);
        Assert.Equal(folderName, manifest!.Id);
    }

    [Fact]
    public void PluginRegistryClient_ValidatesUrlsOwnerRepoSemVerSha256AndOfficialStoreJson()
    {
        Assert.True(PluginRegistryClient.IsAllowedHttpsUrl("https://cdn.jsdelivr.net/gh/a/b@main/store.json"));
        Assert.False(PluginRegistryClient.IsAllowedHttpsUrl("http://insecure.example/a.zip"));
        Assert.False(PluginRegistryClient.IsAllowedHttpsUrl("file:///C:/evil.zip"));

        Assert.True(PluginRegistryClient.TryParseGitHubOwnerRepo("daxiwow/halodeck-fling-trainer", out string owner, out string repo));
        Assert.Equal("daxiwow", owner);
        Assert.Equal("halodeck-fling-trainer", repo);

        Assert.True(PluginRegistryClient.TryParseGitHubOwnerRepo("https://github.com/Lokilankaaa/HaloDeck.git", out owner, out repo));
        Assert.Equal("Lokilankaaa", owner);
        Assert.Equal("HaloDeck", repo);

        Assert.True(PluginRegistryClient.IsNewerVersion("1.1.0", "1.0.9"));
        Assert.True(PluginRegistryClient.IsNewerVersion("v2.0.0", "1.9.9"));
        Assert.False(PluginRegistryClient.IsNewerVersion("1.0.0", "1.0.0"));
        Assert.False(PluginRegistryClient.IsNewerVersion("0.9.5", "1.0.0"));

        // Verify SHA-256 pass & mismatch reject
        string tmpFile = Path.Combine(Path.GetTempPath(), $"halodeck_sha_{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllText(tmpFile, "HaloDeck");
            string realHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(tmpFile)));
            PluginRegistryClient.VerifySha256(tmpFile, realHash);
            Assert.Throws<InvalidOperationException>(() => PluginRegistryClient.VerifySha256(tmpFile, new string('0', 64)));
        }
        finally
        {
            try { File.Delete(tmpFile); } catch { }
        }

        // Verify bundled plugins/store.json parses cleanly and every item has valid id & https zipUrl
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string storePath = Path.Combine(repoRoot, "plugins", "store.json");
        Assert.True(File.Exists(storePath));
        var catalog = JsonSerializer.Deserialize<PluginStoreCatalog>(
            File.ReadAllText(storePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(catalog);
        Assert.Equal(3, catalog!.Plugins.Count);
        Assert.All(catalog.Plugins, item =>
        {
            Assert.Matches(PluginManifest.ValidIdRegex, item.Id);
            Assert.True(PluginRegistryClient.IsAllowedHttpsUrl(item.ZipUrl));
        });
    }

    [Fact]
    public void PluginStoreService_RevokesConsentOnPermissionEscalationDuringUpdate()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "HaloDeck_SecEsc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string zipV1 = Path.Combine(tempRoot, "v1.zip");
        string zipV2 = Path.Combine(tempRoot, "v2.zip");
        try
        {
            using (var za = ZipFile.Open(zipV1, ZipArchiveMode.Create))
            {
                WriteZipEntry(za, "plugin.json", """
                    { "schemaVersion": 1, "id": "test.esc", "name": "Esc Test", "version": "1.0.0", "entry": "ui/index.html", "permissions": ["storage"] }
                    """);
                WriteZipEntry(za, "ui/index.html", "<h1>v1</h1>");
            }
            using (var za = ZipFile.Open(zipV2, ZipArchiveMode.Create))
            {
                WriteZipEntry(za, "plugin.json", """
                    { "schemaVersion": 1, "id": "test.esc", "name": "Esc Test", "version": "2.0.0", "entry": "ui/index.html", "permissions": ["storage", "input:sendKey"] }
                    """);
                WriteZipEntry(za, "ui/index.html", "<h1>v2</h1>");
            }

            var store = new PluginStoreService(Path.Combine(tempRoot, "appdata"));
            store.InstallFromZip(zipV1);
            var enabled = store.SetEnabled("test.esc", true, consentAccepted: true);
            Assert.True(enabled.Enabled);
            Assert.True(enabled.ConsentAccepted);

            // Update to v2 adding new permission input:sendKey -> MUST revoke consent and disable!
            var updated = store.InstallFromZip(zipV2);
            Assert.False(updated.Enabled);
            Assert.False(updated.ConsentAccepted);
            Assert.Throws<InvalidOperationException>(() => store.SetEnabled("test.esc", true, consentAccepted: false));
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public void PluginStoreService_ApplyStoreRevocationsDisablesAndBlocksReEnable()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "HaloDeck_SecRev_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string zipPath = Path.Combine(tempRoot, "skin.zip");
        try
        {
            using (var za = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                WriteZipEntry(za, "plugin.json", """
                    { "schemaVersion": 1, "id": "test.revoked", "name": "Revoked Skin", "version": "1.0.0", "entry": "ui/index.html", "permissions": ["ui:theme"] }
                    """);
                WriteZipEntry(za, "ui/index.html", "<h1>skin</h1>");
            }

            var store = new PluginStoreService(Path.Combine(tempRoot, "appdata"));
            store.InstallFromZip(zipPath);
            store.SetEnabled("test.revoked", true, consentAccepted: true);
            store.SetThemeOverride("test.revoked", new Dictionary<string, string> { ["--accent"] = "#ff0055" });
            Assert.NotNull(store.GetActiveThemeOverride());

            var affected = store.ApplyStoreRevocations(["test.revoked"], "存在安全风险");
            Assert.Contains("test.revoked", affected);
            Assert.Null(store.GetActiveThemeOverride());

            var info = store.GetPlugin("test.revoked")!;
            Assert.False(info.Enabled);
            Assert.True(info.RevokedByStore);
            Assert.Throws<InvalidOperationException>(() => store.SetEnabled("test.revoked", true, consentAccepted: true));
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static void WriteZipEntry(ZipArchive za, string path, string text)
    {
        var e = za.CreateEntry(path);
        using var sw = new StreamWriter(e.Open());
        sw.Write(text);
    }
}
