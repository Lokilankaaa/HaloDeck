namespace HaloDeck.Core.Models;

public enum ComponentSlot
{
    OptiScalerStable,
    OptiScalerNightly,
    OptiPatcher,
    Fakenvapi,
    MfgUnlocker
}

public sealed class ComponentReleaseAsset
{
    public string Name { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public sealed class ComponentInfo
{
    public ComponentSlot Slot { get; set; }
    public string Id => Slot.ToString();
    public string DisplayName { get; set; } = string.Empty;
    public string RepoSlug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string License { get; set; } = "MIT";
    public string Author { get; set; } = string.Empty;
    public string HomepageUrl { get; set; } = string.Empty;
    public bool IsInstalled { get; set; }
    public string? InstalledVersion { get; set; }
    public string? InstalledDirectory { get; set; }
    public DateTimeOffset? InstalledAtUtc { get; set; }
    public string? LatestVersion { get; set; }
    public string? ReleaseUrl { get; set; }
    public string? ReleaseNotes { get; set; }
    public ComponentReleaseAsset? LatestAsset { get; set; }
    public bool HasUpdate => IsInstalled
        && !string.IsNullOrWhiteSpace(LatestVersion)
        && !string.Equals(InstalledVersion, LatestVersion, StringComparison.OrdinalIgnoreCase);
}

public sealed class CustomExtensionFile
{
    public string FileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset AddedAtUtc { get; set; }
}
