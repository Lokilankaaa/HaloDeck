namespace HaloDeck.Core.Models;

public sealed class FlingTrainerSearchResult
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public sealed class FlingTrainerDownload
{
    public string Version { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public bool IsLatest { get; set; }
}

public sealed class DownloadedTrainer
{
    public string Id { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string TrainerTitle { get; set; } = string.Empty;
    public string TrainerVersion { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public DateTimeOffset DownloadedAtUtc { get; set; }
}
