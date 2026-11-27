using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HaloDeck.Core.Plugins;

public sealed class PluginStoreCatalog
{
    public string StoreName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public bool IsOfficial { get; set; }
    public string? Error { get; set; }
    public List<string> RevokedIds { get; set; } = new();
    public List<PluginStoreCatalogItem> Plugins { get; set; } = new();
}

public sealed class PluginStoreCatalogItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Homepage { get; set; } = string.Empty;
    public string Category { get; set; } = "utility";
    public List<string> Mount { get; set; } = new() { "console", "qam" };
    public List<string> Permissions { get; set; } = new();
    public string ZipUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string SourceRepo { get; set; } = string.Empty;
    public string SourceCommit { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public bool IsOfficialSource { get; set; }
}

/// <summary>
/// Zero-server community plugin store client (Decky / Obsidian style).
/// Fetches static JSON catalogs (jsDelivr CDN with GitHub Raw fallback), resolves GitHub owner/repo
/// latest release zips, enforces HTTPS-only downloads + optional SHA-256 verification, and delegates
/// safe installation to <see cref="PluginStoreService"/>.
/// </summary>
public sealed class PluginRegistryClient : IDisposable
{
    public const string OfficialStoreCdnUrl = "https://cdn.jsdelivr.net/gh/Lokilankaaa/HaloDeck@main/plugins/store.json";
    public const string OfficialStoreRawUrl = "https://raw.githubusercontent.com/Lokilankaaa/HaloDeck/main/plugins/store.json";

    private const long MaxZipDownloadBytes = 100L * 1024L * 1024L;
    private static readonly Regex OwnerRepoRegex = new(@"^(?:https?://github\.com/)?(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public PluginRegistryClient(HttpClient? httpClient = null)
    {
        if (httpClient is not null)
        {
            _http = httpClient;
            _ownsHttp = false;
        }
        else
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            string ver = typeof(PluginRegistryClient).Assembly.GetName().Version?.ToString(3) ?? "0.6.2";
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HaloDeck", ver));
            _ownsHttp = true;
        }
    }

    public async Task<List<PluginStoreCatalog>> FetchAllCatalogsAsync(
        IEnumerable<string>? customStoreUrls,
        CancellationToken ct = default)
    {
        var results = new List<PluginStoreCatalog>();

        // 1. Official clean store (jsDelivr CDN first for China-friendly speed, GitHub Raw fallback,
        //    and bundled local plugins/store.json fallback when offline or before remote tag publish)
        var official = await FetchCatalogWithFallbackAsync(
            new[] { OfficialStoreCdnUrl, OfficialStoreRawUrl },
            isOfficial: true,
            fallbackName: "HaloDeck 官方纯净商店",
            ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(official.Error) && official.Plugins.Count == 0)
        {
            var bundled = TryLoadBundledOfficialCatalog();
            if (bundled is not null)
            {
                official = bundled;
            }
        }
        results.Add(official);

        // 2. User-subscribed third-party community store JSONs
        if (customStoreUrls is not null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                OfficialStoreCdnUrl,
                OfficialStoreRawUrl
            };

            foreach (string rawUrl in customStoreUrls)
            {
                string trimmed = (rawUrl ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || !seen.Add(trimmed)) continue;

                if (!IsAllowedHttpsUrl(trimmed))
                {
                    results.Add(new PluginStoreCatalog
                    {
                        StoreName = trimmed,
                        SourceUrl = trimmed,
                        IsOfficial = false,
                        Error = "仅支持 https:// 商店源地址"
                    });
                    continue;
                }

                var custom = await FetchCatalogWithFallbackAsync(
                    new[] { trimmed },
                    isOfficial: false,
                    fallbackName: new Uri(trimmed).Host,
                    ct).ConfigureAwait(false);
                results.Add(custom);
            }
        }

        return results;
    }

    public async Task<InstalledPluginInfo> DownloadAndInstallAsync(
        PluginStoreService storeService,
        string urlOrOwnerRepo,
        string? expectedSha256 = null,
        IProgress<double>? progress = null,
        string? mirrorPrefix = null,
        Action<string>? beforeOverwrite = null,
        CancellationToken ct = default)
    {
        string resolvedZipUrl = await ResolveZipDownloadUrlAsync(urlOrOwnerRepo, ct).ConfigureAwait(false);
        if (!IsAllowedHttpsUrl(resolvedZipUrl))
        {
            throw new InvalidOperationException("插件下载地址必须为合法的 https:// 链接");
        }

        IReadOnlyList<string> candidates = BuildDownloadCandidateUrls(resolvedZipUrl, mirrorPrefix);
        string tempZip = Path.Combine(Path.GetTempPath(), $"HaloDeck_PluginDl_{Guid.NewGuid():N}.zip");
        try
        {
            Exception? lastErr = null;
            bool downloaded = false;

            foreach (string downloadUrl in candidates)
            {
                try
                {
                    using var resp = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();

                    long? contentLen = resp.Content.Headers.ContentLength;
                    if (contentLen is > MaxZipDownloadBytes)
                    {
                        throw new InvalidOperationException("插件包体积超过 100 MB 安全上限");
                    }

                    await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                    await using (var dst = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    {
                        byte[] buf = new byte[81920];
                        long total = 0;
                        int read;
                        while ((read = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                        {
                            total += read;
                            if (total > MaxZipDownloadBytes)
                            {
                                throw new InvalidOperationException("插件包下载体积超过 100 MB 安全上限");
                            }
                            await dst.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                            if (contentLen is > 0)
                            {
                                progress?.Report(Math.Clamp((double)total / contentLen.Value * 100.0, 0, 100));
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(expectedSha256))
                    {
                        VerifySha256(tempZip, expectedSha256);
                    }

                    downloaded = true;
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastErr = ex;
                    try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                }
            }

            if (!downloaded)
            {
                throw new InvalidOperationException($"插件下载失败：{lastErr?.Message ?? "网络异常"}", lastErr);
            }

            // Preserve existing consent if updating an already-consented plugin; new plugins still require consent
            return storeService.InstallFromZip(tempZip, enableAfterInstall: false, consentAccepted: false, beforeOverwrite: beforeOverwrite);
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
        }
    }

    public static string NormalizeMirrorPrefix(string? rawOrPreset)
    {
        string raw = (rawOrPreset ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw) || string.Equals(raw, "direct", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }
        if (string.Equals(raw, "ghproxy", StringComparison.OrdinalIgnoreCase))
        {
            return "https://gh-proxy.com/";
        }
        if (string.Equals(raw, "ghfast", StringComparison.OrdinalIgnoreCase))
        {
            return "https://ghfast.top/";
        }
        if (!IsAllowedHttpsUrl(raw))
        {
            throw new ArgumentException("下载加速镜像前缀必须留空或为合法的 https:// 地址");
        }
        return raw.EndsWith('/') ? raw : raw + "/";
    }

    public static IReadOnlyList<string> BuildDownloadCandidateUrls(string resolvedZipUrl, string? mirrorPrefix)
    {
        var list = new List<string>(2);
        string prefix = (mirrorPrefix ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(prefix) &&
            IsAllowedHttpsUrl(prefix) &&
            resolvedZipUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            string normalizedPrefix = prefix.EndsWith('/') ? prefix : prefix + "/";
            string mirrored = normalizedPrefix + resolvedZipUrl;
            if (IsAllowedHttpsUrl(mirrored))
            {
                list.Add(mirrored);
            }
        }
        list.Add(resolvedZipUrl);
        return list;
    }

    public async Task<string> ResolveZipDownloadUrlAsync(string input, CancellationToken ct = default)
    {
        string trimmed = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("请输入插件 .zip 链接或 GitHub 仓库 (owner/repo)");
        }

        // Direct .zip HTTPS URL
        if (IsAllowedHttpsUrl(trimmed) &&
            (trimmed.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
             trimmed.Contains("/releases/download/", StringComparison.OrdinalIgnoreCase) ||
             trimmed.Contains("/archive/", StringComparison.OrdinalIgnoreCase)))
        {
            return trimmed;
        }

        // GitHub owner/repo or https://github.com/owner/repo -> resolve latest release .zip asset
        if (TryParseGitHubOwnerRepo(trimmed, out string owner, out string repo))
        {
            string apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"无法获取 GitHub 仓库 {owner}/{repo} 的最新 Release (HTTP {(int)resp.StatusCode})");
            }

            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var np) ? (np.GetString() ?? "") : "";
                    string dl = asset.TryGetProperty("browser_download_url", out var dp) ? (dp.GetString() ?? "") : "";
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && IsAllowedHttpsUrl(dl))
                    {
                        return dl;
                    }
                }
            }

            throw new InvalidOperationException($"仓库 {owner}/{repo} 的最新 Release 中未找到 .zip 插件包资产");
        }

        if (IsAllowedHttpsUrl(trimmed))
        {
            return trimmed;
        }

        throw new InvalidOperationException("仅支持 https:// .zip 下载链接或 GitHub 仓库名（如 owner/repo）");
    }

    public static bool TryParseGitHubOwnerRepo(string input, out string owner, out string repo)
    {
        owner = string.Empty;
        repo = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var m = OwnerRepoRegex.Match(input.Trim());
        if (!m.Success) return false;
        owner = m.Groups["owner"].Value;
        repo = m.Groups["repo"].Value;
        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo);
    }

    public static bool IsAllowedHttpsUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(uri.Host);
    }

    public static void VerifySha256(string filePath, string expectedHex)
    {
        string normalizedExpected = expectedHex.Trim().Replace("-", "").ToLowerInvariant();
        if (normalizedExpected.Length != 64)
        {
            throw new InvalidOperationException("SHA-256 校验值格式无效");
        }

        using var sha = SHA256.Create();
        using var fs = File.OpenRead(filePath);
        string actual = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        if (!string.Equals(actual, normalizedExpected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"插件包 SHA-256 校验不匹配（文件可能已损坏或被篡改）");
        }
    }

    public static bool IsNewerVersion(string? candidateVersion, string? installedVersion)
    {
        if (string.IsNullOrWhiteSpace(candidateVersion)) return false;
        if (string.IsNullOrWhiteSpace(installedVersion)) return true;

        static int[] ParseParts(string v) => v.Trim().TrimStart('v', 'V')
            .Split('-', '+')[0]
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.TryParse(p, out int n) ? n : 0)
            .ToArray();

        int[] a = ParseParts(candidateVersion);
        int[] b = ParseParts(installedVersion);
        int len = Math.Max(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            int va = i < a.Length ? a[i] : 0;
            int vb = i < b.Length ? b[i] : 0;
            if (va != vb) return va > vb;
        }
        return false;
    }

    private async Task<PluginStoreCatalog> FetchCatalogWithFallbackAsync(
        string[] candidateUrls,
        bool isOfficial,
        string fallbackName,
        CancellationToken ct)
    {
        string? lastErr = null;
        foreach (string url in candidateUrls)
        {
            try
            {
                string json = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
                var parsed = JsonSerializer.Deserialize<PluginStoreCatalog>(json, JsonOpts)
                             ?? throw new InvalidOperationException("商店清单 JSON 为空");

                parsed.StoreName = string.IsNullOrWhiteSpace(parsed.StoreName) ? fallbackName : parsed.StoreName;
                parsed.SourceUrl = url;
                parsed.IsOfficial = isOfficial;
                parsed.Error = null;
                parsed.Plugins = (parsed.Plugins ?? new List<PluginStoreCatalogItem>())
                    .Where(p => !string.IsNullOrWhiteSpace(p.Id) &&
                                PluginManifest.ValidIdRegex.IsMatch(p.Id) &&
                                IsAllowedHttpsUrl(p.ZipUrl))
                    .Select(p =>
                    {
                        p.SourceName = parsed.StoreName;
                        p.IsOfficialSource = isOfficial;
                        return p;
                    })
                    .ToList();
                return parsed;
            }
            catch (Exception ex)
            {
                lastErr = ex.Message;
            }
        }

        return new PluginStoreCatalog
        {
            StoreName = fallbackName,
            SourceUrl = candidateUrls[0],
            IsOfficial = isOfficial,
            Error = $"拉取商店源失败：{lastErr}"
        };
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    private static PluginStoreCatalog? TryLoadBundledOfficialCatalog()
    {
        try
        {
            string localPath = Path.Combine(AppContext.BaseDirectory, "plugins", "store.json");
            if (!File.Exists(localPath)) return null;
            string json = File.ReadAllText(localPath);
            var catalog = JsonSerializer.Deserialize<PluginStoreCatalog>(json, JsonOpts);
            if (catalog is null || catalog.Plugins.Count == 0) return null;

            catalog.StoreName = string.IsNullOrWhiteSpace(catalog.StoreName)
                ? "HaloDeck 官方纯净商店（本地内置）"
                : $"{catalog.StoreName}（本地预览）";
            catalog.SourceUrl = "local://plugins/store.json";
            catalog.IsOfficial = true;
            catalog.Error = null;
            foreach (var item in catalog.Plugins)
            {
                item.SourceName = catalog.StoreName;
                item.IsOfficialSource = true;
            }
            return catalog;
        }
        catch
        {
            return null;
        }
    }
}
