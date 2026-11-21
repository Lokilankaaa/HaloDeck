using HaloDeck.Core.Models;
using HaloDeck.Core.Process;
using Xunit;

namespace HaloDeck.Tests;

public sealed class HaloDeckV053RegressionTests
{
    [Fact]
    public void AppSettings_MuteGameGamepadInOverlay_DefaultsToFalseForCompatibility()
    {
        var settings = new AppSettings();
        Assert.False(settings.MuteGameGamepadInOverlay);
        Assert.True(settings.ConsoleGamepadCombo.Buttons.Count >= 2);
        Assert.True(settings.QamGamepadCombo.Buttons.Count >= 2);
        Assert.True(settings.InsertGamepadCombo.Buttons.Count >= 2);
        Assert.True(settings.OsdGamepadCombo.Buttons.Count >= 2);
    }

    [Fact]
    public void GameXInputIsolator_RestoreAll_RestoresAndClearsStateCleanly()
    {
        using var isolator = new GameXInputIsolator();
        Assert.False(isolator.HasMutedProcesses);

        // Non-existent / invalid PIDs must never pollute muted tracking
        Assert.False(isolator.TryMuteProcess(0));
        Assert.False(isolator.TryMuteProcess(Environment.ProcessId));
        Assert.False(isolator.HasMutedProcesses);

        // High-risk anti-cheat flag always bypasses remote thread muting
        Assert.False(isolator.TryMuteProcess(999999, knownHighRiskAntiCheat: true));
        Assert.False(isolator.HasMutedProcesses);

        isolator.RestoreAll();
        Assert.False(isolator.HasMutedProcesses);
    }
}
