using System.Buffers.Binary;
using HandheldOpti.Core.Analysis;
using HandheldOpti.Core.Injection;
using HandheldOpti.Core.Models;
using HandheldOpti.Core.Process;
using Xunit;

namespace HandheldOpti.Tests;

public sealed class ExecutableResolverAndAntiCheatTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"ho_res_{Guid.NewGuid():N}");

    [Fact]
    public void Resolve_UnrealEngineGame_FindsWin64ShippingAndPeArchitectureAndDlss()
    {
        var gameRoot = Path.Combine(_tempRoot, "SampleUE5Game");
        var engineCrashDir = Path.Combine(gameRoot, "Engine", "Binaries", "Win64");
        var shippingDir = Path.Combine(gameRoot, "MyGame", "Binaries", "Win64");
        var pluginDlssDir = Path.Combine(gameRoot, "Engine", "Plugins", "Runtime", "Nvidia", "DLSS", "Binaries", "ThirdParty", "Win64");

        Directory.CreateDirectory(engineCrashDir);
        Directory.CreateDirectory(shippingDir);
        Directory.CreateDirectory(pluginDlssDir);

        WriteMinimalPe64Exe(Path.Combine(gameRoot, "MyGame.exe"), sizeBytes: 1024);
        WriteMinimalPe64Exe(Path.Combine(engineCrashDir, "CrashReportClient.exe"), sizeBytes: 4096);
        var shippingExe = Path.Combine(shippingDir, "MyGame-Win64-Shipping.exe");
        WriteMinimalPe64Exe(shippingExe, sizeBytes: 65536);
        File.WriteAllText(Path.Combine(shippingDir, "nvngx_dlss.dll"), "stub");

        var layout = ExecutableResolver.Resolve(gameRoot);

        Assert.Equal(GameEngine.Unreal, layout.Engine);
        Assert.Equal(shippingDir, layout.TargetRenderDirectory);
        Assert.Equal(shippingExe, layout.MainExecutablePath);
        Assert.Equal(PeArchitecture.X64, layout.Architecture);
        Assert.True(layout.Upscalers.HasDlss);
        Assert.True(layout.IsWritable);
    }

    [Fact]
    public void AntiCheatDetector_DetectsEac_AndBlocksBackgroundAutoInjection()
    {
        var gameRoot = Path.Combine(_tempRoot, "OnlineEacGame");
        var eacDir = Path.Combine(gameRoot, "EasyAntiCheat");
        Directory.CreateDirectory(eacDir);
        File.WriteAllText(Path.Combine(eacDir, "EasyAntiCheat_EOS.exe"), "eac");
        File.WriteAllText(Path.Combine(gameRoot, "nvngx_dlss.dll"), "dlss");

        var antiCheat = AntiCheatDetector.Detect(gameRoot);
        Assert.True(antiCheat.HasAntiCheat);
        Assert.Equal(AntiCheatRiskLevel.HighRisk, antiCheat.RiskLevel);
        Assert.Contains(antiCheat.DetectedSystems, s => s.Contains("Easy Anti-Cheat"));

        var game = new GameEntry
        {
            Id = "custom:eac",
            Title = "Online Game",
            InstallDirectory = gameRoot,
            TargetRenderDirectory = gameRoot,
            Architecture = PeArchitecture.X64,
            IsWritable = true,
            AntiCheat = antiCheat,
            NativeUpscalers = new UpscalerCapabilities { HasDlss = true }
        };

        var settings = new AppSettings { BackgroundAutoInjectEnabled = true };
        Assert.False(BackgroundAutoInjector.IsEligibleForAutoPreInject(game, settings));
    }

    private static void WriteMinimalPe64Exe(string path, int sizeBytes)
    {
        var bytes = new byte[Math.Max(sizeBytes, 0x100)];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        int peOffset = 0x80;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3C, 4), peOffset);
        bytes[peOffset] = (byte)'P';
        bytes[peOffset + 1] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOffset + 4, 2), 0x8664); // AMD64
        File.WriteAllBytes(path, bytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true);
    }
}
