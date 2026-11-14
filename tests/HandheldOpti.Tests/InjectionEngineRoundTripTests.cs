using System.IO.Compression;
using HandheldOpti.Core.Components;
using HandheldOpti.Core.Config;
using HandheldOpti.Core.Injection;
using HandheldOpti.Core.Models;
using Xunit;

namespace HandheldOpti.Tests;

public sealed class InjectionEngineRoundTripTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"ho_inj_{Guid.NewGuid():N}");

    [Fact]
    public async Task AutoMode_AvoidsThirdPartyDxgiConflict_DeploysPlugins_AndRestoresCleanlyWithSha256()
    {
        var appData = Path.Combine(_tempRoot, "AppData");
        var staging = Path.Combine(_tempRoot, "Staging");
        var gameDir = Path.Combine(_tempRoot, "Game", "Binaries", "Win64");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(gameDir);

        // 1. Create simulated OptiScaler.zip with nested root folder, companion DLL, Licenses, README, & template OptiScaler.ini
        var optiZip = CreateZipPackage(
            Path.Combine(staging, "OptiScaler.zip"),
            new Dictionary<string, string>
            {
                ["OptiScaler_v0.9.4/OptiScaler.dll"] = "OPTISCALER_BINARY_PAYLOAD_V094",
                ["OptiScaler_v0.9.4/OptiScaler.ini"] = "; Official Header\n[Upscalers]\n;Dx12Upscaler=auto\n",
                ["OptiScaler_v0.9.4/setup_windows.bat"] = "@echo skip",
                ["OptiScaler_v0.9.4/!! README_EXTRACT ALL FILES TO GAME FOLDER !!.txt"] = "skip readme",
                ["OptiScaler_v0.9.4/Licenses/Apache.txt"] = "skip license",
                ["OptiScaler_v0.9.4/Licenses/LICENSE.bin"] = "skip license binary",
                ["OptiScaler_v0.9.4/libxess.dll"] = "NEW_OPTISCALER_LIBXESS_DLL",
                ["OptiScaler_v0.9.4/D3D12_Optiscaler/D3D12Core.dll"] = "D3D12_CORE_HELPER"
            });

        var patcherZip = CreateZipPackage(
            Path.Combine(staging, "OptiPatcher.zip"),
            new Dictionary<string, string>
            {
                ["OptiPatcher.asi"] = "OPTIPATCHER_ASI_PAYLOAD"
            });

        var fakeZip = CreateZipPackage(
            Path.Combine(staging, "fakenvapi.zip"),
            new Dictionary<string, string>
            {
                ["fakenvapi.dll"] = "FAKENVAPI_DLL_PAYLOAD",
                ["fakenvapi.ini"] = "[Settings]\nEnableLogs=0\n"
            });

        var cm = new ComponentManager(appData);
        cm.ImportLocalPackage(ComponentSlot.OptiScalerStable, optiZip, "v0.9.4");
        cm.ImportLocalPackage(ComponentSlot.OptiPatcher, patcherZip, "v0.3.1");
        cm.ImportLocalPackage(ComponentSlot.Fakenvapi, fakeZip, "v1.4.1");

        // 2. Pre-place third-party ReShade dxgi.dll AND pre-existing game libxess.dll
        var reshadeDxgiPath = Path.Combine(gameDir, "dxgi.dll");
        await File.WriteAllTextAsync(reshadeDxgiPath, "RESHADE_THIRD_PARTY_ORIGINAL_CONTENT");
        var originalDxgiSha = InjectionEngine.ComputeSha256(reshadeDxgiPath);

        var gameLibXessPath = Path.Combine(gameDir, "libxess.dll");
        await File.WriteAllTextAsync(gameLibXessPath, "GAME_ORIGINAL_LIBXESS_V1");
        var originalXessSha = InjectionEngine.ComputeSha256(gameLibXessPath);

        var game = new GameEntry
        {
            Id = "custom:testgame",
            Title = "Test Game",
            InstallDirectory = gameDir,
            TargetRenderDirectory = gameDir,
            Architecture = PeArchitecture.X64,
            IsWritable = true
        };

        var engine = new InjectionEngine(cm, appData);
        var preset = PresetCatalog.GetById("strix-halo-quality");

        var injectRes = await engine.InjectAsync(game, preset.Config, nameof(ComponentSlot.OptiScalerStable), preset.Id);

        Assert.True(injectRes.Success, injectRes.Message);
        Assert.True(injectRes.ConflictAvoided);
        Assert.Equal("winmm.dll", injectRes.SelectedProxyDll);
        Assert.True(File.Exists(Path.Combine(gameDir, "winmm.dll")));
        Assert.True(File.Exists(Path.Combine(gameDir, "plugins", "OptiPatcher.asi")));
        Assert.True(File.Exists(Path.Combine(gameDir, "fakenvapi.dll")));
        Assert.True(File.Exists(Path.Combine(gameDir, "D3D12_Optiscaler", "D3D12Core.dll")));
        Assert.Equal("NEW_OPTISCALER_LIBXESS_DLL", await File.ReadAllTextAsync(gameLibXessPath));
        Assert.False(File.Exists(Path.Combine(gameDir, "setup_windows.bat")));
        Assert.False(File.Exists(Path.Combine(gameDir, "!! README_EXTRACT ALL FILES TO GAME FOLDER !!.txt")));
        Assert.False(Directory.Exists(Path.Combine(gameDir, "Licenses")));
        Assert.Equal(originalDxgiSha, InjectionEngine.ComputeSha256(reshadeDxgiPath));

        var writtenIni = IniDocument.Load(Path.Combine(gameDir, "OptiScaler.ini"));
        Assert.Equal("true", writtenIni.Get("FrameGen", "Enabled"));
        Assert.Equal("upscaler", writtenIni.Get("FrameGen", "FGInput"));
        Assert.Equal("xefg", writtenIni.Get("FrameGen", "FGOutput"));
        Assert.Equal("fsr31", writtenIni.Get("Upscalers", "Dx12Upscaler"));
        Assert.Equal("true", writtenIni.Get("FSR", "Fsr4ForceEnableInt8"));
        Assert.Equal("auto", writtenIni.Get("Spoofing", "Dxgi"));
        Assert.Equal("true", writtenIni.Get("Plugins", "LoadAsiPlugins"));
        Assert.Equal("true", writtenIni.Get("NvApi", "OverrideNvapiDll"));
        Assert.Equal("0x2D", writtenIni.Get("Menu", "ShortcutKey"));

        // Re-inject with EnableOptiPatcher = false and ensure OptiPatcher.asi is removed while original libxess backup remains intact
        var reCfg = preset.Config.Clone();
        reCfg.EnableOptiPatcher = false;
        var reInjectRes = await engine.InjectAsync(game, reCfg, nameof(ComponentSlot.OptiScalerStable), preset.Id);
        Assert.True(reInjectRes.Success, reInjectRes.Message);
        Assert.False(File.Exists(Path.Combine(gameDir, "plugins", "OptiPatcher.asi")));

        // 3. Restore clean directory and verify complete removal + ReShade untouched + original libxess SHA256 restored
        var restoreRes = await engine.RestoreCleanAsync(game);
        Assert.True(restoreRes.Success, restoreRes.Message);
        Assert.False(File.Exists(Path.Combine(gameDir, "winmm.dll")));
        Assert.False(File.Exists(Path.Combine(gameDir, "OptiScaler.ini")));
        Assert.False(File.Exists(Path.Combine(gameDir, "fakenvapi.dll")));
        Assert.False(Directory.Exists(Path.Combine(gameDir, "D3D12_Optiscaler")));
        Assert.False(Directory.Exists(Path.Combine(gameDir, "plugins")));
        Assert.False(Directory.Exists(Path.Combine(gameDir, ".handheldopti_backup")));
        Assert.Equal(originalDxgiSha, InjectionEngine.ComputeSha256(reshadeDxgiPath));
        Assert.True(File.Exists(gameLibXessPath));
        Assert.Equal(originalXessSha, InjectionEngine.ComputeSha256(gameLibXessPath));
    }

    [Fact]
    public async Task ExplicitProxyOverwrite_BacksUpOriginalAndRestoresExactSha256()
    {
        var appData = Path.Combine(_tempRoot, "AppData2");
        var staging = Path.Combine(_tempRoot, "Staging2");
        var gameDir = Path.Combine(_tempRoot, "Game2");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(gameDir);

        var optiZip = CreateZipPackage(
            Path.Combine(staging, "OptiScaler.zip"),
            new Dictionary<string, string> { ["OptiScaler.dll"] = "NEW_OPTISCALER" });

        var cm = new ComponentManager(appData);
        cm.ImportLocalPackage(ComponentSlot.OptiScalerStable, optiZip, "v0.7.9");

        var existingWinmm = Path.Combine(gameDir, "winmm.dll");
        await File.WriteAllTextAsync(existingWinmm, "ORIGINAL_GAME_WINMM_DLL");
        var originalSha = InjectionEngine.ComputeSha256(existingWinmm);

        var game = new GameEntry
        {
            Id = "custom:explicit",
            Title = "Explicit Proxy Game",
            InstallDirectory = gameDir,
            TargetRenderDirectory = gameDir,
            Architecture = PeArchitecture.X64,
            IsWritable = true
        };

        var cfg = new OptiScalerConfig { ProxyDllName = "winmm.dll", EnableOptiPatcher = false, EnableFakenvapi = false };
        var engine = new InjectionEngine(cm, appData);

        var injectRes = await engine.InjectAsync(game, cfg, nameof(ComponentSlot.OptiScalerStable), "custom");
        Assert.True(injectRes.Success);
        Assert.Equal("NEW_OPTISCALER", await File.ReadAllTextAsync(existingWinmm));

        var restoreRes = await engine.RestoreCleanAsync(game);
        Assert.True(restoreRes.Success);
        Assert.True(File.Exists(existingWinmm));
        Assert.Equal(originalSha, InjectionEngine.ComputeSha256(existingWinmm));
    }

    private static string CreateZipPackage(string zipPath, Dictionary<string, string> files)
    {
        using var fs = new FileStream(zipPath, FileMode.Create);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (relPath, content) in files)
        {
            var entry = archive.CreateEntry(relPath);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
        return zipPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true);
    }
}
