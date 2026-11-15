using HaloDeck.Core.Config;
using HaloDeck.Core.Injection;
using HaloDeck.Core.Models;

namespace HaloDeck.Core.Process;

public sealed record AutoInjectionEvent(string GameId, string GameTitle, bool Success, string Message);

/// <summary>
/// Optional background pre-injector that automatically equips eligible library games
/// with the default RDNA 3.5 preset while strictly skipping anti-cheat titles and excluded games.
/// </summary>
public sealed class BackgroundAutoInjector
{
    private readonly InjectionEngine _injectionEngine;

    public BackgroundAutoInjector(InjectionEngine injectionEngine)
    {
        _injectionEngine = injectionEngine;
    }

    public async Task<IReadOnlyList<AutoInjectionEvent>> RunPassAsync(
        IReadOnlyList<GameEntry> libraryGames,
        AppSettings settings,
        CancellationToken ct = default)
    {
        if (!settings.BackgroundAutoInjectEnabled)
        {
            return Array.Empty<AutoInjectionEvent>();
        }

        var preset = PresetCatalog.GetById(settings.DefaultPresetId);
        var events = new List<AutoInjectionEvent>();

        foreach (var game in libraryGames)
        {
            ct.ThrowIfCancellationRequested();

            if (!IsEligibleForAutoPreInject(game, settings))
            {
                continue;
            }

            var result = await _injectionEngine.InjectAsync(
                game,
                preset.Config,
                settings.ActiveOptiScalerSlot,
                preset.Id,
                settings.DisabledCustomDlls,
                allowAntiCheatOverride: false,
                ct).ConfigureAwait(false);

            events.Add(new AutoInjectionEvent(game.Id, game.Title, result.Success, result.Message));
        }

        return events;
    }

    public static bool IsEligibleForAutoPreInject(GameEntry game, AppSettings settings)
    {
        if (game.Injection.IsInjected) return false;
        if (game.IsCurrentlyRunning) return false; // Never modify files while game is actively running silently
        if (!game.IsWritable) return false;
        if (game.Architecture == PeArchitecture.X86) return false;
        if (game.AntiCheat.HasAntiCheat) return false; // Hard stop for safety
        if (game.AutoInjectExcluded || settings.ExcludedGameIds.Contains(game.Id, StringComparer.OrdinalIgnoreCase)) return false;

        // Only auto-preinject games that actually contain a known upscaler/Streamline hook target
        return game.NativeUpscalers.HasDlss ||
               game.NativeUpscalers.HasXeSs ||
               game.NativeUpscalers.HasFsr ||
               game.NativeUpscalers.HasStreamline;
    }
}
