using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using HaloDeck.Core.Config;
using HaloDeck.Core.Models;

namespace HaloDeck.Core.Components;

public sealed class ComponentManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _appDataRoot;
    private readonly string _componentsRoot;
    private readonly string _customDllsRoot;
    private readonly string _stateFilePath;
    private readonly HttpClient _http;
    private readonly Dictionary<ComponentSlot, ComponentInfo> _components;

    public ComponentManager(string? appDataRoot = null, HttpMessageHandler? httpHandler = null)
    {
        _appDataRoot = HaloDeckPaths.ResolveAppDataRoot(appDataRoot);
        _componentsRoot = Path.Combine(_appDataRoot, "components");
        _customDllsRoot = Path.Combine(_appDataRoot, "custom_dlls");
        _stateFilePath = Path.Combine(_componentsRoot, "components_state.json");

        Directory.CreateDirectory(_componentsRoot);
        Directory.CreateDirectory(_customDllsRoot);

        _http = httpHandler is null ? new HttpClient() : new HttpClient(httpHandler);
        _http.Timeout = TimeSpan.FromMinutes(3);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("HaloDeck/0.4.2");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        _components = CreateDefaultCatalog();
        LoadPersistedState();
        SyncInstalledDirectories();
    }

    public string CustomDllsRoot => _customDllsRoot;

    public IReadOnlyList<ComponentInfo> GetComponents() => _components.Values.OrderBy(c => (int)c.Slot).ToList();

    public ComponentInfo GetComponent(ComponentSlot slot) => _components[slot];

    public string? ResolveActiveOptiScalerRoot(string activeSlotName)
    {
        if (Enum.TryParse<ComponentSlot>(activeSlotName, out var preferred) &&
            (preferred == ComponentSlot.OptiScalerStable || preferred == ComponentSlot.OptiScalerNightly))
        {
            var preferredInfo = _components[preferred];
            if (preferredInfo.IsInstalled && !string.IsNullOrWhiteSpace(preferredInfo.InstalledDirectory) &&
                Directory.Exists(preferredInfo.InstalledDirectory))
            {
                return ArchiveExtractor.ResolvePayloadRoot(preferredInfo.InstalledDirectory);
            }
        }

        foreach (var fallback in new[] { ComponentSlot.OptiScalerStable, ComponentSlot.OptiScalerNightly })
        {
            var info = _components[fallback];
            if (info.IsInstalled && !string.IsNullOrWhiteSpace(info.InstalledDirectory) &&
                Directory.Exists(info.InstalledDirectory))
            {
                return ArchiveExtractor.ResolvePayloadRoot(info.InstalledDirectory);
            }
        }

        return null;
    }

    public string? ResolveOptiPatcherAsiPath()
    {
        var info = _components[ComponentSlot.OptiPatcher];
        if (!info.IsInstalled || string.IsNullOrWhiteSpace(info.InstalledDirectory) || !Directory.Exists(info.InstalledDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(info.InstalledDirectory, "*.asi", SearchOption.AllDirectories)
            .OrderByDescending(f => Path.GetFileName(f).Contains("OptiPatcher", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
    }

    public string? ResolveFakenvapiRoot()
    {
        var info = _components[ComponentSlot.Fakenvapi];
        if (!info.IsInstalled || string.IsNullOrWhiteSpace(info.InstalledDirectory) || !Directory.Exists(info.InstalledDirectory))
        {
            return null;
        }

        return ArchiveExtractor.ResolvePayloadRoot(info.InstalledDirectory);
    }

    public async Task<IReadOnlyList<ComponentInfo>> RefreshCatalogAsync(CancellationToken ct = default)
    {
        TryAttachOptionalGitHubToken();

        foreach (var info in _components.Values)
        {
            try
            {
                await FetchLatestReleaseAsync(info, ct).ConfigureAwait(false);
            }
            catch
            {
                // Keep cached metadata if offline or rate-limited
            }
        }

        SavePersistedState();
        return GetComponents();
    }

    public async Task<ComponentInfo> InstallOrUpdateAsync(
        ComponentSlot slot,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var info = _components[slot];
        if (info.LatestAsset is null)
        {
            TryAttachOptionalGitHubToken();
            await FetchLatestReleaseAsync(info, ct).ConfigureAwait(false);
        }

        if (info.LatestAsset is null || string.IsNullOrWhiteSpace(info.LatestAsset.DownloadUrl))
        {
            throw new InvalidOperationException($"未能获取 {info.DisplayName} 的可用发布文件，请检查网络或手动导入离线压缩包。");
        }

        var downloadsDir = Path.Combine(_componentsRoot, "_downloads");
        Directory.CreateDirectory(downloadsDir);
        var downloadPath = Path.Combine(downloadsDir, info.LatestAsset.Name);

        using (var response = await _http.GetAsync(info.LatestAsset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? info.LatestAsset.SizeBytes;

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                totalRead += read;
                if (totalBytes > 0)
                {
                    progress?.Report(Math.Clamp((double)totalRead / totalBytes * 85.0, 0, 85));
                }
            }
        }

        progress?.Report(90);
        var installDir = InstallFromLocalFileInternal(slot, downloadPath, info.LatestVersion ?? "downloaded");
        progress?.Report(100);
        return _components[slot];
    }

    public ComponentInfo ImportLocalPackage(ComponentSlot slot, string localFilePath, string? versionLabel = null)
    {
        if (!File.Exists(localFilePath))
        {
            throw new FileNotFoundException("找不到导入文件", localFilePath);
        }

        var label = versionLabel ?? $"local-{Path.GetFileNameWithoutExtension(localFilePath)}";
        InstallFromLocalFileInternal(slot, localFilePath, label);
        return _components[slot];
    }

    public IReadOnlyList<CustomExtensionFile> GetCustomExtensions(IEnumerable<string>? disabledNames = null)
    {
        var disabled = disabledNames is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(disabledNames, StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(_customDllsRoot))
        {
            return Array.Empty<CustomExtensionFile>();
        }

        return Directory.EnumerateFiles(_customDllsRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(p =>
            {
                var fi = new FileInfo(p);
                return new CustomExtensionFile
                {
                    FileName = fi.Name,
                    StoredPath = fi.FullName,
                    SizeBytes = fi.Length,
                    Enabled = !disabled.Contains(fi.Name),
                    AddedAtUtc = fi.CreationTimeUtc
                };
            })
            .OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public CustomExtensionFile ImportCustomExtensionFile(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("扩展文件不存在", sourcePath);
        }

        var fileName = Path.GetFileName(sourcePath);
        var destPath = Path.Combine(_customDllsRoot, fileName);
        File.Copy(sourcePath, destPath, overwrite: true);

        var fi = new FileInfo(destPath);
        return new CustomExtensionFile
        {
            FileName = fi.Name,
            StoredPath = fi.FullName,
            SizeBytes = fi.Length,
            Enabled = true,
            AddedAtUtc = fi.CreationTimeUtc
        };
    }

    public void DeleteCustomExtensionFile(string fileName)
    {
        var target = Path.Combine(_customDllsRoot, Path.GetFileName(fileName));
        if (File.Exists(target))
        {
            File.Delete(target);
        }
    }

    private string InstallFromLocalFileInternal(ComponentSlot slot, string localFilePath, string versionLabel)
    {
        var targetDir = Path.Combine(_componentsRoot, slot.ToString());
        var ext = Path.GetExtension(localFilePath).ToLowerInvariant();

        if (ext is ".7z" or ".zip")
        {
            ArchiveExtractor.ExtractToCleanDirectory(localFilePath, targetDir);
        }
        else if (ext is ".dll" or ".asi" or ".ini")
        {
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
            Directory.CreateDirectory(targetDir);
            File.Copy(localFilePath, Path.Combine(targetDir, Path.GetFileName(localFilePath)), overwrite: true);
        }
        else
        {
            throw new NotSupportedException($"不支持的组件格式: {ext}（仅支持 .7z / .zip / .dll / .asi）");
        }

        var info = _components[slot];
        info.IsInstalled = true;
        info.InstalledVersion = versionLabel;
        info.InstalledDirectory = targetDir;
        info.InstalledAtUtc = DateTimeOffset.UtcNow;
        SavePersistedState();
        return targetDir;
    }

    private async Task FetchLatestReleaseAsync(ComponentInfo info, CancellationToken ct)
    {
        // Try /releases/latest first; for nightly or pre-release-only repos (e.g. OptiScaler-nightly / OptiPatcher),
        // fallback to /releases?per_page=5 and select the newest release containing a usable asset.
        JsonElement? selectedRelease = null;

        try
        {
            using var latestResp = await _http.GetAsync($"https://api.github.com/repos/{info.RepoSlug}/releases/latest", ct).ConfigureAwait(false);
            if (latestResp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await latestResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                selectedRelease = doc.RootElement.Clone();
            }
        }
        catch
        {
            // Fallback below
        }

        if (selectedRelease is null)
        {
            using var listResp = await _http.GetAsync($"https://api.github.com/repos/{info.RepoSlug}/releases?per_page=5", ct).ConfigureAwait(false);
            listResp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                selectedRelease = doc.RootElement[0].Clone();
            }
        }

        if (selectedRelease is not JsonElement release)
        {
            return;
        }

        info.LatestVersion = release.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
        info.ReleaseUrl = release.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null;
        info.ReleaseNotes = release.TryGetProperty("body", out var body) ? Truncate(body.GetString(), 600) : null;

        if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            info.LatestAsset = SelectBestAsset(info.Slot, assets);
        }
    }

    private static ComponentReleaseAsset? SelectBestAsset(ComponentSlot slot, JsonElement assets)
    {
        var list = new List<ComponentReleaseAsset>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
            var size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }
            if (name.Contains("source", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            list.Add(new ComponentReleaseAsset { Name = name, DownloadUrl = url, SizeBytes = size });
        }

        if (list.Count == 0)
        {
            return null;
        }

        if (slot == ComponentSlot.OptiPatcher)
        {
            return list.FirstOrDefault(a => a.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(a => a.Name.EndsWith(".asi", StringComparison.OrdinalIgnoreCase))
                ?? list[0];
        }

        return list.FirstOrDefault(a => a.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) && !a.Name.Contains("debug", StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !a.Name.Contains("debug", StringComparison.OrdinalIgnoreCase))
            ?? list[0];
    }

    private void SyncInstalledDirectories()
    {
        foreach (var info in _components.Values)
        {
            var expectedDir = Path.Combine(_componentsRoot, info.Slot.ToString());
            if (Directory.Exists(expectedDir) && Directory.EnumerateFiles(expectedDir, "*", SearchOption.AllDirectories).Any())
            {
                info.IsInstalled = true;
                info.InstalledDirectory = expectedDir;
                info.InstalledVersion ??= "local";
            }
            else
            {
                info.IsInstalled = false;
                info.InstalledDirectory = null;
            }
        }
    }

    private void TryAttachOptionalGitHubToken()
    {
        if (_http.DefaultRequestHeaders.Authorization is not null)
        {
            return;
        }

        var envToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                    ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", envToken.Trim());
            return;
        }

        try
        {
            var ghPath = @"C:\Program Files\GitHub CLI\gh.exe";
            if (!File.Exists(ghPath))
            {
                return;
            }

            using var proc = System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = ghPath,
                Arguments = "auth token",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (proc is null)
            {
                return;
            }
            if (proc.WaitForExit(1500) && proc.ExitCode == 0)
            {
                var token = proc.StandardOutput.ReadToEnd().Trim();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
            }
        }
        catch
        {
            // Optional best-effort token lookup
        }
    }

    private void LoadPersistedState()
    {
        if (!File.Exists(_stateFilePath))
        {
            return;
        }

        try
        {
            var saved = JsonSerializer.Deserialize<List<ComponentInfo>>(File.ReadAllText(_stateFilePath), JsonOptions);
            if (saved is null)
            {
                return;
            }

            foreach (var item in saved)
            {
                if (_components.TryGetValue(item.Slot, out var existing))
                {
                    existing.IsInstalled = item.IsInstalled;
                    existing.InstalledVersion = item.InstalledVersion;
                    existing.InstalledDirectory = item.InstalledDirectory;
                    existing.InstalledAtUtc = item.InstalledAtUtc;
                    existing.LatestVersion = item.LatestVersion;
                    existing.ReleaseUrl = item.ReleaseUrl;
                    existing.ReleaseNotes = item.ReleaseNotes;
                    existing.LatestAsset = item.LatestAsset;
                }
            }
        }
        catch
        {
            // Ignore corrupt state cache
        }
    }

    private void SavePersistedState()
    {
        try
        {
            Directory.CreateDirectory(_componentsRoot);
            File.WriteAllText(_stateFilePath, JsonSerializer.Serialize(_components.Values.ToList(), JsonOptions));
        }
        catch
        {
            // Ignore write errors
        }
    }

    private static Dictionary<ComponentSlot, ComponentInfo> CreateDefaultCatalog() => new()
    {
        [ComponentSlot.OptiScalerStable] = new ComponentInfo
        {
            Slot = ComponentSlot.OptiScalerStable,
            DisplayName = "OptiScaler (Stable)",
            RepoSlug = "optiscaler/OptiScaler",
            Description = "官方稳定版超分与帧生成中间件，支持 DLSS / XeSS / FSR / OptiFG 转换。"
        },
        [ComponentSlot.OptiScalerNightly] = new ComponentInfo
        {
            Slot = ComponentSlot.OptiScalerNightly,
            DisplayName = "OptiScaler (Nightly)",
            RepoSlug = "optiscaler/OptiScaler-nightly",
            Description = "官方每日测试构建，包含最新 FSR4 / OptiFG / 游戏兼容性修复。"
        },
        [ComponentSlot.OptiPatcher] = new ComponentInfo
        {
            Slot = ComponentSlot.OptiPatcher,
            DisplayName = "OptiPatcher (ASI 插件)",
            RepoSlug = "optiscaler/OptiPatcher",
            Description = "免 DXGI 显卡伪装直接解锁游戏内置 DLSS & DLSS-G 菜单选项，强烈建议搭配 AMD Strix Halo 使用。"
        },
        [ComponentSlot.Fakenvapi] = new ComponentInfo
        {
            Slot = ComponentSlot.Fakenvapi,
            DisplayName = "Fakenvapi (Anti-Lag 2 桥接)",
            RepoSlug = "optiscaler/fakenvapi",
            Description = "在 AMD 8060S/8050S 等非 N 卡上将 NVIDIA Reflex 转换为 AMD Anti-Lag 2，并辅助开启 DLSS-G 桥接。"
        }
    };

    private static string? Truncate(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLen) return text;
        return text[..maxLen] + "...";
    }
}
