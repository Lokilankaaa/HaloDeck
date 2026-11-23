using HaloDeck.Core.Config;
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
        Assert.False(settings.EnableBackButtonHotkeys);
        Assert.True(settings.InterceptPhysicalMenuButton);
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

    [Fact]
    public void SettingsStore_MigratesLegacyBackButtonHotkeyDefaultOnceAndPreservesUserOptIn()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "HaloDeckTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new SettingsStore(tempDir);
            File.WriteAllText(store.SettingsFilePath, """
                {
                  "enableBackButtonHotkeys": true,
                  "currentTdpWatts": 45
                }
                """);

            // Legacy v0.5.3 file without schema version migrates back-button F14-F16 takeover to false once
            // while keeping physical GPD Win 5 F13 quick-menu button active
            var migrated = store.Load();
            Assert.False(migrated.EnableBackButtonHotkeys);
            Assert.True(migrated.InterceptPhysicalMenuButton);
            Assert.Equal(1, migrated.BackButtonHotkeysSchemaVersion);

            // Explicit user opt-in is preserved across subsequent Save/Load
            migrated.EnableBackButtonHotkeys = true;
            store.Save(migrated);

            var reloaded = store.Load();
            Assert.True(reloaded.EnableBackButtonHotkeys);
            Assert.Equal(1, reloaded.BackButtonHotkeysSchemaVersion);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
