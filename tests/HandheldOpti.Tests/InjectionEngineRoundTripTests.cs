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

        // 1. Create simulated OptiScaler.zip with nested root folder & template OptiScaler.ini
        var optiZip = CreateZipPackage(
            Path.Combine(staging, "OptiScaler.zip"),
            new Dictionary<string, string>
            {
                ["OptiScaler_v0.7.9/OptiScaler.dll"] = "OPTISCALER_BINARY_PAYLOAD_V079",
                ["OptiScaler_v0.7.9/OptiScaler.ini"] = "; Official Header\n[Upscalers]\n;Dx12Upscaler=auto\n",
                ["OptiScaler_v0.7.9/setup_windows.bat"] = "@echo skip",
                ["OptiScaler_v0.7.9/D3D12_Optiscaler/amd_fidelityfx_dx12.dll"] = "FFX_HELPER"
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
        cm.ImportLocalPackage(ComponentSlot.OptiScalerStable, optiZip, "v0.7.9");
        cm.ImportLocalPackage(ComponentSlot.OptiPatcher, patcherZip, "v0.3.1");
        cm.ImportLocalPackage(ComponentSlot.Fakenvapi, fakeZip, "v1.4.1");

        // 2. Pre-place third-party ReShade dxgi.dll in target game directory
        var reshadeDxgiPath = Path.Combine(gameDir, "dxgi.dll");
        await File.WriteAllTextAsync(reshadeDxgiPath, "RESHADE_THIRD_PARTY_ORIGINAL_CONTENT");
        var originalDxgiSha = InjectionEngine.ComputeSha256(reshadeDxgiPath);

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
        Assert.True(File.Exists(Path.Combine(gameDir, "D3D12_Optiscaler", "amd_fidelityfx_dx12.dll")));
        Assert.False(File.Exists(Path.Combine(gameDir, "setup_windows.bat")));
        Assert.Equal(originalDxgiSha, InjectionEngine.ComputeSha256(reshadeDxgiPath));

        var writtenIni = IniDocument.Load(Path.Combine(gameDir, "OptiScaler.ini"));
        Assert.Equal("true", writtenIni.Get("FrameGen", "Enabled"));
        Assert.Equal("upscaler", writtenIni.Get("FrameGen", "FGInput"));
        Assert.Equal("fsrfg", writtenIni.Get("FrameGen", "FGOutput"));
        Assert.Equal("true", writtenIni.Get("Plugins", "LoadAsiPlugins"));
        Assert.Equal("true", writtenIni.Get("NvApi", "OverrideNvapiDll"));
        Assert.Equal("0x2D", writtenIni.Get("Menu", "ShortcutKey"));

        // 3. Restore clean directory and verify complete removal + ReShade untouched
        var restoreRes = await engine.RestoreCleanAsync(game);
        Assert.True(restoreRes.Success, restoreRes.Message);
        Assert.False(File.Exists(Path.Combine(gameDir, "winmm.dll")));
        Assert.False(File.Exists(Path.Combine(gameDir, "OptiScaler.ini")));
        Assert.False(File.Exists(Path.Combine(gameDir, "fakenvapi.dll")));
        Assert.False(Directory.Exists(Path.Combine(gameDir, "plugins")));
        Assert.False(Directory.Exists(Path.Combine(gameDir, ".handheldopti_backup")));
        Assert.Equal(originalDxgiSha, InjectionEngine.ComputeSha256(reshadeDxgiPath));
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
