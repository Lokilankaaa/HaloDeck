using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HaloDeck.Core.Plugins;

namespace HaloDeck.Core.Services;

public sealed class AppUpdateInfo
{
    public string CurrentVersion { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string Sha256Url { get; set; } = string.Empty;
    public long AssetSizeBytes { get; set; }
    public bool IsPrerelease { get; set; }
    public bool HasUpdate { get; set; }
    public string PublishedAt { get; set; } = string.Empty;
    public DateTimeOffset CheckedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StagedAppUpdate
{
    public string StageRoot { get; set; } = string.Empty;
    public string PayloadDir { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string VerifiedSha256 { get; set; } = string.Empty;
}

/// <summary>
/// Safe, zero-dependency HaloDeck self-updater backed by GitHub Releases.
/// Supports stable & optional pre-release channels, optional HTTPS GitHub mirror prefix
/// with automatic direct fallback, mandatory SHA-256 verification when published,
/// and Zip-Slip-safe staging for in-place portable replacement.
/// </summary>
public sealed class AppUpdateService : IDisposable
{
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/Lokilankaaa/HaloDeck/releases/latest";
    public const string ReleasesListApiUrl = "https://api.github.com/repos/Lokilankaaa/HaloDeck/releases?per_page=10";
    public const string ReleasesPageUrl = "https://github.com/Lokilankaaa/HaloDeck/releases";

    private const long MaxUpdateZipBytes = 350L * 1024L * 1024L;
    private static readonly Regex Sha256HexRegex = new(@"\b[a-fA-F0-9]{64}\b", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public AppUpdateService(HttpClient? httpClient = null)
    {
        if (httpClient is not null)
        {
            _http = httpClient;
            _ownsHttp = false;
        }
        else
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            string ver = typeof(AppUpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.6.2";
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HaloDeck", ver));
            _ownsHttp = true;
        }
    }

    public async Task<AppUpdateInfo> CheckForUpdateAsync(
        string currentVersion,
        bool includePrerelease = false,
        CancellationToken ct = default)
    {
        string normalizedCurrent = NormalizeVersionTag(currentVersion);
        string apiUrl = includePrerelease ? ReleasesListApiUrl : LatestReleaseApiUrl;

        using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"检查更新失败：GitHub API 返回 HTTP {(int)resp.StatusCode}");
        }

        string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        JsonElement targetRelease = default;
        bool found = false;

        if (includePrerelease && doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                if (rel.TryGetProperty("draft", out var dr) && dr.ValueKind == JsonValueKind.True) continue;
                string tag = rel.TryGetProperty("tag_name", out var tp) ? (tp.GetString() ?? "") : "";
                if (!tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ||
                    tag.Equals("plugin-store-builds", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                targetRelease = rel;
                found = true;
                break;
            }
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            targetRelease = doc.RootElement;
            found = true;
        }

        if (!found)
        {
            return new AppUpdateInfo
            {
                CurrentVersion = normalizedCurrent,
                LatestVersion = normalizedCurrent,
                HtmlUrl = ReleasesPageUrl,
                HasUpdate = false,
                CheckedAtUtc = DateTimeOffset.UtcNow
            };
        }

        string tagName = targetRelease.TryGetProperty("tag_name", out var tagProp) ? (tagProp.GetString() ?? "") : "";
        string latestVer = NormalizeVersionTag(tagName);
        string title = targetRelease.TryGetProperty("name", out var nameProp) ? (nameProp.GetString() ?? tagName) : tagName;
        string body = targetRelease.TryGetProperty("body", out var bodyProp) ? (bodyProp.GetString() ?? "") : "";
        string htmlUrl = targetRelease.TryGetProperty("html_url", out var htmlProp) ? (htmlProp.GetString() ?? ReleasesPageUrl) : ReleasesPageUrl;
        string publishedAt = targetRelease.TryGetProperty("published_at", out var pubProp) ? (pubProp.GetString() ?? "") : "";
        bool isPre = targetRelease.TryGetProperty("prerelease", out var preProp) && preProp.ValueKind == JsonValueKind.True;

        string downloadUrl = string.Empty;
        string sha256Url = string.Empty;
        long assetSize = 0;

        if (targetRelease.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string assetName = asset.TryGetProperty("name", out var np) ? (np.GetString() ?? "") : "";
                string dl = asset.TryGetProperty("browser_download_url", out var dp) ? (dp.GetString() ?? "") : "";
                if (!PluginRegistryClient.IsAllowedHttpsUrl(dl)) continue;

                if (assetName.Equals("HaloDeck-win-x64-portable.zip", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = dl;
                    assetSize = asset.TryGetProperty("size", out var sp) && sp.ValueKind == JsonValueKind.Number ? sp.GetInt64() : 0;
                }
                else if (assetName.Equals("HaloDeck-win-x64-portable.zip.sha256", StringComparison.OrdinalIgnoreCase))
                {
                    sha256Url = dl;
                }
            }
        }

        bool hasUpdate = CompareVersions(latestVer, normalizedCurrent) > 0;
        return new AppUpdateInfo
        {
            CurrentVersion = normalizedCurrent,
            LatestVersion = latestVer,
            TagName = tagName,
            Title = title,
            ReleaseNotes = body.Length > 2400 ? body[..2400] + "\n..." : body,
            HtmlUrl = PluginRegistryClient.IsAllowedHttpsUrl(htmlUrl) ? htmlUrl : ReleasesPageUrl,
            DownloadUrl = downloadUrl,
            Sha256Url = sha256Url,
            AssetSizeBytes = assetSize,
            IsPrerelease = isPre,
            HasUpdate = hasUpdate,
            PublishedAt = publishedAt,
            CheckedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task<StagedAppUpdate> DownloadAndStageUpdateAsync(
        AppUpdateInfo info,
        string? mirrorPrefix = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (info is null || !PluginRegistryClient.IsAllowedHttpsUrl(info.DownloadUrl))
        {
            throw new InvalidOperationException("该版本未提供有效的便携版下载链接，请前往 GitHub Releases 手动下载");
        }

        string? expectedSha256 = null;
        if (PluginRegistryClient.IsAllowedHttpsUrl(info.Sha256Url))
        {
            expectedSha256 = await TryFetchExpectedSha256Async(info.Sha256Url, mirrorPrefix, ct).ConfigureAwait(false);
        }

        string stageRoot = Path.Combine(Path.GetTempPath(), $"HaloDeck_Update_{Guid.NewGuid():N}");
        string zipPath = Path.Combine(stageRoot, "HaloDeck-win-x64-portable.zip");
        string payloadDir = Path.Combine(stageRoot, "payload");
        Directory.CreateDirectory(stageRoot);

        try
        {
            var candidates = PluginRegistryClient.BuildDownloadCandidateUrls(info.DownloadUrl, mirrorPrefix);
            Exception? lastErr = null;
            bool downloaded = false;

            foreach (string url in candidates)
            {
                try
                {
                    using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();

                    long? contentLen = resp.Content.Headers.ContentLength;
                    if (contentLen is > MaxUpdateZipBytes)
                    {
                        throw new InvalidOperationException("更新包体积超过安全上限");
                    }

                    await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                    await using (var dst = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    {
                        byte[] buf = new byte[81920];
                        long total = 0;
                        int read;
                        while ((read = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                        {
                            total += read;
                            if (total > MaxUpdateZipBytes)
                            {
                                throw new InvalidOperationException("更新包下载体积超过安全上限");
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
                        PluginRegistryClient.VerifySha256(zipPath, expectedSha256);
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
                    try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
                }
            }

            if (!downloaded)
            {
                throw new InvalidOperationException($"更新包下载失败：{lastErr?.Message ?? "网络异常"}", lastErr);
            }

            ExtractZipSafely(zipPath, payloadDir);
            string exePath = Path.Combine(payloadDir, "HaloDeck.exe");
            if (!File.Exists(exePath))
            {
                throw new InvalidOperationException("更新包结构异常：解压后未找到 HaloDeck.exe");
            }

            try { File.Delete(zipPath); } catch { }
            return new StagedAppUpdate
            {
                StageRoot = stageRoot,
                PayloadDir = payloadDir,
                Version = info.LatestVersion,
                VerifiedSha256 = expectedSha256 ?? string.Empty
            };
        }
        catch
        {
            try { if (Directory.Exists(stageRoot)) Directory.Delete(stageRoot, true); } catch { }
            throw;
        }
    }

    public static string WritePortableUpdaterScript(StagedAppUpdate staged, string targetAppDir, int currentPid)
    {
        string scriptPath = Path.Combine(staged.StageRoot, "apply-update.ps1");
        string escapedPayload = staged.PayloadDir.Replace("'", "''");
        string escapedTarget = targetAppDir.TrimEnd('\\', '/').Replace("'", "''");
        string escapedStage = staged.StageRoot.Replace("'", "''");

        string script = $$"""
            $ErrorActionPreference = 'Continue'
            $targetPid = {{currentPid}}
            $payload = '{{escapedPayload}}'
            $target = '{{escapedTarget}}'
            $stageRoot = '{{escapedStage}}'

            for ($i = 0; $i -lt 50; $i++) {
                $p = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
                if (-not $p) { break }
                Start-Sleep -Milliseconds 200
            }

            Start-Sleep -Milliseconds 300
            $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
            $lockedDir = Join-Path $target ".old_locked_$stamp"

            Get-ChildItem -Path $payload -Recurse -File | ForEach-Object {
                $rel = $_.FullName.Substring($payload.Length).TrimStart('\')
                $dest = Join-Path $target $rel
                $destParent = Split-Path -Parent $dest
                if (-not (Test-Path $destParent)) { New-Item -ItemType Directory -Force -Path $destParent | Out-Null }
                try {
                    Copy-Item -Path $_.FullName -Destination $dest -Force -ErrorAction Stop
                } catch {
                    if (Test-Path $dest) {
                        $moved = Join-Path $lockedDir $rel
                        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $moved) | Out-Null
                        try { Move-Item -Path $dest -Destination $moved -Force -ErrorAction Stop } catch {}
                        try { Copy-Item -Path $_.FullName -Destination $dest -Force -ErrorAction Stop } catch {}
                    }
                }
            }

            $exe = Join-Path $target 'HaloDeck.exe'
            if (Test-Path $exe) {
                Start-Process -FilePath $exe -WorkingDirectory $target
            }

            Start-Sleep -Seconds 2
            Remove-Item -Path $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
            """;

        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return scriptPath;
    }

    public static string NormalizeVersionTag(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "0.0.0";
        string cleaned = version.Trim().TrimStart('v', 'V');
        int plusIdx = cleaned.IndexOf('+');
        if (plusIdx >= 0) cleaned = cleaned[..plusIdx];
        return string.IsNullOrWhiteSpace(cleaned) ? "0.0.0" : cleaned;
    }

    /// <summary>
    /// SemVer comparison supporting pre-release suffixes: e.g. 0.6.2 &gt; 0.6.2-rc.1 &gt; 0.6.1.
    /// </summary>
    public static int CompareVersions(string? a, string? b)
    {
        static (int[] Core, string Pre) Parse(string? raw)
        {
            string norm = NormalizeVersionTag(raw);
            int dash = norm.IndexOf('-');
            string coreStr = dash >= 0 ? norm[..dash] : norm;
            string pre = dash >= 0 ? norm[(dash + 1)..] : string.Empty;
            int[] parts = coreStr.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out int n) ? n : 0)
                .ToArray();
            return (parts, pre);
        }

        var pa = Parse(a);
        var pb = Parse(b);
        int maxLen = Math.Max(pa.Core.Length, pb.Core.Length);
        for (int i = 0; i < maxLen; i++)
        {
            int va = i < pa.Core.Length ? pa.Core[i] : 0;
            int vb = i < pb.Core.Length ? pb.Core[i] : 0;
            if (va != vb) return va.CompareTo(vb);
        }

        bool aPre = !string.IsNullOrEmpty(pa.Pre);
        bool bPre = !string.IsNullOrEmpty(pb.Pre);
        if (!aPre && !bPre) return 0;
        if (!aPre && bPre) return 1;  // stable > prerelease
        if (aPre && !bPre) return -1;
        return string.Compare(pa.Pre, pb.Pre, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> TryFetchExpectedSha256Async(string shaUrl, string? mirrorPrefix, CancellationToken ct)
    {
        foreach (string url in PluginRegistryClient.BuildDownloadCandidateUrls(shaUrl, mirrorPrefix))
        {
            try
            {
                string text = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
                var m = Sha256HexRegex.Match(text);
                if (m.Success) return m.Value.ToLowerInvariant();
            }
            catch
            {
                // try next candidate
            }
        }
        return null;
    }

    private static void ExtractZipSafely(string zipPath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        string fullRoot = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            string destPath = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
            if (!destPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"更新包包含非法路径：{entry.FullName}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
