using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HandheldOpti.Core.Analysis;
using HandheldOpti.Core.Injection;
using HandheldOpti.Core.Models;

namespace HandheldOpti.Core.Discovery;

public sealed class GameLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SteamLibraryScanner _steamScanner = new();
    private readonly EpicGogXboxScanner _otherScanner = new();
    private readonly InjectionEngine _injectionEngine;
    private readonly string _customGamesPath;

    public GameLibraryService(InjectionEngine injectionEngine, string? appDataRoot = null)
    {
        _injectionEngine = injectionEngine;
        var root = appDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HandheldOpti");
        Directory.CreateDirectory(root);
        _customGamesPath = Path.Combine(root, "custom_games.json");
    }

    public async Task<IReadOnlyList<GameEntry>> ScanAllAsync(IEnumerable<string>? excludedIds = null, CancellationToken ct = default)
    {
        var excluded = excludedIds is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(excludedIds, StringComparer.OrdinalIgnoreCase);

        var steamTask = Task.Run(() => _steamScanner.Scan(), ct);
        var otherTask = Task.Run(() => _otherScanner.ScanAll(), ct);
        var customList = LoadCustomGames();

        await Task.WhenAll(steamTask, otherTask).ConfigureAwait(false);

        var rawGames = new List<GameEntry>();
        rawGames.AddRange(steamTask.Result);
        rawGames.AddRange(otherTask.Result);
        rawGames.AddRange(customList);

        var deduped = new Dictionary<string, GameEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in rawGames)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(game.InstallDirectory) || !Directory.Exists(game.InstallDirectory))
            {
                continue;
            }

            var normDir = Path.GetFullPath(game.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (!deduped.ContainsKey(normDir))
            {
                EnrichGame(game, excluded);
                deduped[normDir] = game;
            }
        }

        return deduped.Values
            .OrderByDescending(g => g.IsCurrentlyRunning)
            .ThenByDescending(g => g.Injection.IsInjected)
            .ThenByDescending(g => g.NativeUpscalers.HasAnyUpscaler)
            .ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public GameEntry EnrichGame(GameEntry game, HashSet<string>? excluded = null)
    {
        var layout = ExecutableResolver.Resolve(game.InstallDirectory, game.MainExecutablePath);
        game.Engine = layout.Engine;
        game.TargetRenderDirectory = layout.TargetRenderDirectory;
        if (!string.IsNullOrWhiteSpace(layout.MainExecutablePath))
        {
            game.MainExecutablePath = layout.MainExecutablePath;
        }
        game.Architecture = layout.Architecture;
        game.NativeUpscalers = layout.Upscalers;
        game.IsWritable = layout.IsWritable;
        game.AntiCheat = AntiCheatDetector.Detect(game.InstallDirectory, game.TargetRenderDirectory);
        game.AutoInjectExcluded = excluded?.Contains(game.Id) ?? false;

        RefreshInjectionStatus(game);
        return game;
    }

    public void RefreshInjectionStatus(GameEntry game)
    {
        var manifest = _injectionEngine.ReadManifest(game.TargetRenderDirectory);
        if (manifest is not null)
        {
            game.Injection = new InjectionStatus
            {
                IsInjected = true,
                ProxyDllName = manifest.ProxyDllName,
                PresetId = manifest.PresetId,
                OptiScalerVersion = manifest.OptiScalerVersion,
                HasOptiPatcher = manifest.HasOptiPatcher,
                HasFakenvapi = manifest.HasFakenvapi,
                InjectedAtUtc = manifest.InjectedAtUtc,
                LastLogSnippet = InjectionEngine.ReadOptiScalerLogSnippet(game.TargetRenderDirectory)
            };
        }
        else
        {
            game.Injection = new InjectionStatus { IsInjected = false };
        }
    }

    public GameEntry AddCustomGame(string pathInput, string? customTitle = null)
    {
        string installDir;
        string? exePath = null;

        if (File.Exists(pathInput))
        {
            exePath = Path.GetFullPath(pathInput);
            installDir = Path.GetDirectoryName(exePath)!;
        }
        else if (Directory.Exists(pathInput))
        {
            installDir = Path.GetFullPath(pathInput);
        }
        else
        {
            throw new DirectoryNotFoundException($"找不到目标游戏路径: {pathInput}");
        }

        var idHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(installDir.ToLowerInvariant())))[..12];
        var title = !string.IsNullOrWhiteSpace(customTitle)
            ? customTitle.Trim()
            : exePath is not null
                ? Path.GetFileNameWithoutExtension(exePath)
                : Path.GetFileName(installDir.TrimEnd(Path.DirectorySeparatorChar));

        var entry = new GameEntry
        {
            Id = $"custom:{idHash}",
            Title = title,
            Platform = GamePlatform.Custom,
            InstallDirectory = installDir,
            TargetRenderDirectory = installDir,
            MainExecutablePath = exePath
        };

        EnrichGame(entry);

        var existing = LoadCustomGames();
        existing.RemoveAll(g => string.Equals(g.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        existing.Add(entry);
        SaveCustomGames(existing);
        return entry;
    }

    public void RemoveCustomGame(string gameId)
    {
        var existing = LoadCustomGames();
        if (existing.RemoveAll(g => string.Equals(g.Id, gameId, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            SaveCustomGames(existing);
        }
    }

    private List<GameEntry> LoadCustomGames()
    {
        if (!File.Exists(_customGamesPath))
        {
            return new List<GameEntry>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<GameEntry>>(File.ReadAllText(_customGamesPath), JsonOptions) ?? new List<GameEntry>();
        }
        catch
        {
            return new List<GameEntry>();
        }
    }

    private void SaveCustomGames(List<GameEntry> list)
    {
        File.WriteAllText(_customGamesPath, JsonSerializer.Serialize(list, JsonOptions));
    }
}
