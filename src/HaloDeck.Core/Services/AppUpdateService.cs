using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
    public const string ReleasesAtomUrl = "https://github.com/Lokilankaaa/HaloDeck/releases.atom";
    public const string LatestReleaseWebUrl = "https://github.com/Lokilankaaa/HaloDeck/releases/latest";
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/Lokilankaaa/HaloDeck/releases/latest";
    public const string ReleasesListApiUrl = "https://api.github.com/repos/Lokilankaaa/HaloDeck/releases?per_page=10";
    public const string ReleasesPageUrl = "https://github.com/Lokilankaaa/HaloDeck/releases";

    private const long MaxUpdateZipBytes = 350L * 1024L * 1024L;
    private static readonly Regex Sha256HexRegex = new(@"\b[a-fA-F0-9]{64}\b", RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex TagInUrlRegex = new(@"/releases/tag/(?<tag>[^/?#]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        string? mirrorPrefix = null,
        CancellationToken ct = default)
    {
        string normalizedCurrent = NormalizeVersionTag(currentVersion);
        var errors = new List<string>();

        // 1. Primary: GitHub Releases Atom feed (zero API rate limit, never hits 403 on shared IPs,
        //    returns both stable & pre-release versions with titles and release notes).
        foreach (string atomUrl in PluginRegistryClient.BuildDownloadCandidateUrls(ReleasesAtomUrl, mirrorPrefix))
        {
            try
            {
                string xml = await _http.GetStringAsync(atomUrl, ct).ConfigureAwait(false);
                var fromAtom = ParseReleasesAtomFeed(xml, normalizedCurrent, includePrerelease);
                if (fromAtom is not null)
                {
                    return fromAtom;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                errors.Add($"Atom({ex.Message})");
            }
        }

        // 2. Fallback (stable channel): GitHub /releases/latest redirect (also zero API rate limit).
        if (!includePrerelease)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, LatestReleaseWebUrl);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                string finalUrl = resp.RequestMessage?.RequestUri?.AbsoluteUri ?? string.Empty;
                var m = TagInUrlRegex.Match(finalUrl);
                if (resp.IsSuccessStatusCode && m.Success)
                {
                    string tag = Uri.UnescapeDataString(m.Groups["tag"].Value);
                    return BuildInfoFromTag(normalizedCurrent, tag, $"HaloDeck {tag}", "", finalUrl, "", IsPrereleaseTag(tag));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                errors.Add($"Redirect({ex.Message})");
            }
        }

        // 3. Final fallback: GitHub REST API (may be rate-limited 403 on shared exit IPs).
        try
        {
            return await CheckViaGitHubRestApiAsync(normalizedCurrent, includePrerelease, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            errors.Add($"API({ex.Message})");
            throw new InvalidOperationException($"检查更新失败，请检查网络或配置镜像前缀：{errors[0]}");
        }
    }

    public static AppUpdateInfo? ParseReleasesAtomFeed(string atomXml, string normalizedCurrent, bool includePrerelease)
    {
        if (string.IsNullOrWhiteSpace(atomXml)) return null;
        var doc = XDocument.Parse(atomXml);
        XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        AppUpdateInfo? best = null;
        foreach (var entry in doc.Descendants(ns + "entry"))
        {
            string linkHref = entry.Elements(ns + "link")
                .Select(l => (string?)l.Attribute("href") ?? "")
                .FirstOrDefault(h => h.Contains("/releases/tag/", StringComparison.OrdinalIgnoreCase)) ?? "";

            string tag = string.Empty;
            var m = TagInUrlRegex.Match(linkHref);
            if (m.Success)
            {
                tag = Uri.UnescapeDataString(m.Groups["tag"].Value);
            }
            else
            {
                string idVal = (string?)entry.Element(ns + "id") ?? "";
                int lastSlash = idVal.LastIndexOf('/');
                if (lastSlash >= 0 && lastSlash + 1 < idVal.Length)
                {
                    tag = idVal[(lastSlash + 1)..];
                }
            }

            if (string.IsNullOrWhiteSpace(tag) ||
                !tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("plugin-store-builds", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool isPre = IsPrereleaseTag(tag);
            if (isPre && !includePrerelease) continue;

            string title = ((string?)entry.Element(ns + "title") ?? tag).Trim();
            string updated = ((string?)entry.Element(ns + "updated") ?? "").Trim();
            string rawHtml = (string?)entry.Element(ns + "content") ?? "";
            string notes = StripHtmlToPlainText(rawHtml);
            string htmlUrl = PluginRegistryClient.IsAllowedHttpsUrl(linkHref)
                ? linkHref
                : $"https://github.com/Lokilankaaa/HaloDeck/releases/tag/{Uri.EscapeDataString(tag)}";

            var candidate = BuildInfoFromTag(normalizedCurrent, tag, title, notes, htmlUrl, updated, isPre);
            if (best is null || CompareVersions(candidate.LatestVersion, best.LatestVersion) > 0)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static AppUpdateInfo BuildInfoFromTag(
        string normalizedCurrent,
        string tagName,
        string title,
        string notes,
        string htmlUrl,
        string publishedAt,
        bool isPrerelease)
    {
        string latestVer = NormalizeVersionTag(tagName);
        string escapedTag = Uri.EscapeDataString(tagName);
        string dlBase = $"https://github.com/Lokilankaaa/HaloDeck/releases/download/{escapedTag}";
        return new AppUpdateInfo
        {
            CurrentVersion = normalizedCurrent,
            LatestVersion = latestVer,
            TagName = tagName,
            Title = string.IsNullOrWhiteSpace(title) ? tagName : title,
            ReleaseNotes = notes.Length > 2400 ? notes[..2400] + "\n..." : notes,
            HtmlUrl = PluginRegistryClient.IsAllowedHttpsUrl(htmlUrl) ? htmlUrl : ReleasesPageUrl,
            DownloadUrl = $"{dlBase}/HaloDeck-win-x64-portable.zip",
            Sha256Url = $"{dlBase}/HaloDeck-win-x64-portable.zip.sha256",
            IsPrerelease = isPrerelease,
            HasUpdate = CompareVersions(latestVer, normalizedCurrent) > 0,
            PublishedAt = publishedAt,
            CheckedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static bool IsPrereleaseTag(string tag)
        => tag.Contains("-rc", StringComparison.OrdinalIgnoreCase) ||
           tag.Contains("-beta", StringComparison.OrdinalIgnoreCase) ||
           tag.Contains("-alpha", StringComparison.OrdinalIgnoreCase);

    private static string StripHtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        string s = Regex.Replace(html, @"<\s*(br|p|div|li|h[1-6]|tr)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        s = HtmlTagRegex.Replace(s, "");
        s = WebUtility.HtmlDecode(s);
        var lines = s.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);
        return string.Join("\n", lines);
    }

    private async Task<AppUpdateInfo> CheckViaGitHubRestApiAsync(
        string normalizedCurrent,
        bool includePrerelease,
        CancellationToken ct)
    {
        string apiUrl = includePrerelease ? ReleasesListApiUrl : LatestReleaseApiUrl;

        using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GitHub API 返回 HTTP {(int)resp.StatusCode}");
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
                // Support zips packaged with a single top-level wrapper folder
                string[] subDirs = Directory.GetDirectories(payloadDir);
                if (subDirs.Length == 1)
                {
                    string nestedExe = Path.Combine(subDirs[0], "HaloDeck.exe");
                    if (File.Exists(nestedExe))
                    {
                        payloadDir = subDirs[0];
                        exePath = nestedExe;
                    }
                }
            }
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

    public static string WritePortableUpdaterScript(
        StagedAppUpdate staged,
        string targetAppDir,
        int currentPid,
        bool launchAfterApply = true)
    {
        string scriptPath = Path.Combine(staged.StageRoot, "apply-update.ps1");
        string escapedPayload = staged.PayloadDir.TrimEnd('\\', '/').Replace("'", "''");
        string escapedTarget = Path.GetFullPath(targetAppDir).TrimEnd('\\', '/').Replace("'", "''");
        string escapedStage = staged.StageRoot.TrimEnd('\\', '/').Replace("'", "''");
        string launchFlag = launchAfterApply ? "$true" : "$false";

        string script = $$"""
            $ErrorActionPreference = 'Continue'
            $targetPid = {{currentPid}}
            $payload = '{{escapedPayload}}'
            $target = '{{escapedTarget}}'
            $stageRoot = '{{escapedStage}}'
            $launchAfterApply = {{launchFlag}}

            $logDir = Join-Path (Join-Path $env:LOCALAPPDATA 'HaloDeck') 'logs'
            $logPath = Join-Path $logDir 'last-update.log'
            function Write-UpdateLog([string]$msg) {
                try {
                    if (-not [System.IO.Directory]::Exists($logDir)) {
                        [System.IO.Directory]::CreateDirectory($logDir) | Out-Null
                    }
                    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')] $msg`r`n"
                    [System.IO.File]::AppendAllText($logPath, $line, [System.Text.Encoding]::UTF8)
                } catch {}
            }

            Write-UpdateLog "Updater started (PID=$targetPid, payload=$payload, target=$target)"

            for ($i = 0; $i -lt 75; $i++) {
                $p = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
                if (-not $p) { break }
                Start-Sleep -Milliseconds 200
            }

            Start-Sleep -Milliseconds 400
            $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
            $lockedDir = [System.IO.Path]::Combine($target, ".old_locked_$stamp")
            $payloadExe = [System.IO.Path]::Combine($payload, 'HaloDeck.exe')
            $targetExe = [System.IO.Path]::Combine($target, 'HaloDeck.exe')

            if (-not [System.IO.File]::Exists($payloadExe) -or ([System.IO.FileInfo]$payloadExe).Length -le 0) {
                Write-UpdateLog "ABORT: staged payload HaloDeck.exe missing or empty: $payloadExe"
                if ($launchAfterApply -and [System.IO.File]::Exists($targetExe)) {
                    try { Start-Process -FilePath $targetExe -WorkingDirectory $target } catch {}
                }
                exit 1
            }

            # Best-effort cleanup of stale .old_locked_* folders from previous runs
            try {
                foreach ($oldDir in [System.IO.Directory]::GetDirectories($target, '.old_locked_*')) {
                    try { [System.IO.Directory]::Delete($oldDir, $true) } catch {}
                }
            } catch {}

            function Clear-ReadOnlyAttr([string]$filePath) {
                try {
                    if ([System.IO.File]::Exists($filePath)) {
                        $attr = [System.IO.File]::GetAttributes($filePath)
                        if (($attr -band [System.IO.FileAttributes]::ReadOnly) -ne 0) {
                            [System.IO.File]::SetAttributes($filePath, ($attr -band (-bnot [System.IO.FileAttributes]::ReadOnly)))
                        }
                    }
                } catch {}
            }

            function Replace-FileSafely([string]$src, [string]$dest, [string]$rel) {
                if (-not [System.IO.File]::Exists($src)) {
                    Write-UpdateLog "SKIP missing source: $src"
                    return $false
                }

                $srcLen = ([System.IO.FileInfo]$src).Length
                $destParent = [System.IO.Path]::GetDirectoryName($dest)
                if (-not [System.IO.Directory]::Exists($destParent)) {
                    [System.IO.Directory]::CreateDirectory($destParent) | Out-Null
                }

                # 1. Stage-first inside destination folder before touching $dest
                $tmp = "$dest.hd_upd_tmp"
                $stagedOk = $false
                for ($s = 0; $s -lt 5; $s++) {
                    try {
                        Clear-ReadOnlyAttr $tmp
                        [System.IO.File]::Copy($src, $tmp, $true)
                        if ([System.IO.File]::Exists($tmp) -and ([System.IO.FileInfo]$tmp).Length -eq $srcLen) {
                            $stagedOk = $true
                            break
                        }
                    } catch {
                        Write-UpdateLog "Stage retry #$s failed for ${rel}: $($_.Exception.Message)"
                    }
                    Start-Sleep -Milliseconds 250
                }

                if (-not $stagedOk) {
                    try { if ([System.IO.File]::Exists($tmp)) { [System.IO.File]::Delete($tmp) } } catch {}
                    Write-UpdateLog "FAILED staging $rel; existing destination left untouched"
                    return $false
                }

                Clear-ReadOnlyAttr $dest

                # 2. Atomic swap / overwrite with retries and mandatory rollback if locked-move fails
                for ($attempt = 0; $attempt -lt 6; $attempt++) {
                    try {
                        if (-not [System.IO.File]::Exists($dest)) {
                            [System.IO.File]::Move($tmp, $dest)
                            if ([System.IO.File]::Exists($dest) -and ([System.IO.FileInfo]$dest).Length -eq $srcLen) {
                                return $true
                            }
                        } else {
                            try {
                                [System.IO.File]::Copy($tmp, $dest, $true)
                                if ([System.IO.File]::Exists($dest) -and ([System.IO.FileInfo]$dest).Length -eq $srcLen) {
                                    try { [System.IO.File]::Delete($tmp) } catch {}
                                    return $true
                                }
                            } catch {
                                # Destination is actively locked (e.g. HaloDeckHardwareSvc holding HaloDeck.exe):
                                # park it in .old_locked_<stamp>, promote $tmp -> $dest, or rollback immediately on failure.
                                $moved = [System.IO.Path]::Combine($lockedDir, $rel)
                                $movedParent = [System.IO.Path]::GetDirectoryName($moved)
                                if (-not [System.IO.Directory]::Exists($movedParent)) {
                                    [System.IO.Directory]::CreateDirectory($movedParent) | Out-Null
                                }
                                if ([System.IO.File]::Exists($moved)) {
                                    try { [System.IO.File]::Delete($moved) } catch {}
                                }

                                $movedOut = $false
                                try {
                                    [System.IO.File]::Move($dest, $moved)
                                    $movedOut = $true
                                } catch {
                                    Write-UpdateLog "Move-out locked $rel attempt #$attempt failed: $($_.Exception.Message)"
                                }

                                if ($movedOut) {
                                    $promoted = $false
                                    for ($m = 0; $m -lt 4; $m++) {
                                        try {
                                            if ([System.IO.File]::Exists($dest)) {
                                                [System.IO.File]::Delete($dest)
                                            }
                                            [System.IO.File]::Move($tmp, $dest)
                                            if ([System.IO.File]::Exists($dest) -and ([System.IO.FileInfo]$dest).Length -eq $srcLen) {
                                                $promoted = $true
                                                break
                                            }
                                        } catch {
                                            Write-UpdateLog "Promote retry #$m failed for ${rel}: $($_.Exception.Message)"
                                        }
                                        Start-Sleep -Milliseconds 150
                                    }

                                    if ($promoted) {
                                        Write-UpdateLog "Replaced locked file $rel (parked old copy in .old_locked_$stamp)"
                                        return $true
                                    }

                                    # Mandatory rollback so $dest is NEVER left missing
                                    if ((-not [System.IO.File]::Exists($dest)) -and [System.IO.File]::Exists($moved)) {
                                        for ($rb = 0; $rb -lt 4; $rb++) {
                                            try {
                                                [System.IO.File]::Move($moved, $dest)
                                                Write-UpdateLog "ROLLBACK restored $rel after swap failure"
                                                break
                                            } catch {
                                                Start-Sleep -Milliseconds 150
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    } catch {
                        Write-UpdateLog "Replace attempt #$attempt exception for ${rel}: $($_.Exception.Message)"
                    }

                    Start-Sleep -Milliseconds (200 + $attempt * 100)
                }

                try { if ([System.IO.File]::Exists($tmp)) { [System.IO.File]::Delete($tmp) } } catch {}
                Write-UpdateLog "FAILED replacing $rel after all retries"
                return $false
            }

            $allFiles = [System.IO.Directory]::GetFiles($payload, '*', [System.IO.SearchOption]::AllDirectories) |
                Sort-Object { $_.Equals($payloadExe, [System.StringComparison]::OrdinalIgnoreCase) }

            $failures = 0
            foreach ($srcFile in $allFiles) {
                $rel = $srcFile.Substring($payload.Length).TrimStart('\', '/')
                $dest = [System.IO.Path]::Combine($target, $rel)
                if (-not (Replace-FileSafely $srcFile $dest $rel)) {
                    $failures++
                }
            }

            # Clean up any leftover temp staging files in $target
            try {
                foreach ($stray in [System.IO.Directory]::GetFiles($target, '*.hd_upd_tmp', [System.IO.SearchOption]::AllDirectories)) {
                    try { [System.IO.File]::Delete($stray) } catch {}
                }
            } catch {}

            # Post-apply invariant: target HaloDeck.exe MUST exist; try direct recovery, then rollback from .old_locked_*
            if (-not [System.IO.File]::Exists($targetExe)) {
                Write-UpdateLog "WARNING: HaloDeck.exe missing after apply pass; attempting recovery"
                for ($r = 0; $r -lt 3; $r++) {
                    if (Replace-FileSafely $payloadExe $targetExe 'HaloDeck.exe') { break }
                    Start-Sleep -Milliseconds 300
                }
            }

            if ((-not [System.IO.File]::Exists($targetExe)) -and [System.IO.Directory]::Exists($lockedDir)) {
                Write-UpdateLog "EMERGENCY ROLLBACK: restoring missing files from $lockedDir"
                foreach ($oldFile in [System.IO.Directory]::GetFiles($lockedDir, '*', [System.IO.SearchOption]::AllDirectories)) {
                    $relOld = $oldFile.Substring($lockedDir.Length).TrimStart('\', '/')
                    $restoreDest = [System.IO.Path]::Combine($target, $relOld)
                    if (-not [System.IO.File]::Exists($restoreDest)) {
                        try {
                            $rp = [System.IO.Path]::GetDirectoryName($restoreDest)
                            if (-not [System.IO.Directory]::Exists($rp)) {
                                [System.IO.Directory]::CreateDirectory($rp) | Out-Null
                            }
                            [System.IO.File]::Move($oldFile, $restoreDest)
                        } catch {}
                    }
                }
            }

            $exeReady = [System.IO.File]::Exists($targetExe)
            Write-UpdateLog "Updater finished (exeReady=$exeReady, failures=$failures)"

            if ($launchAfterApply -and $exeReady) {
                Start-Process -FilePath $targetExe -WorkingDirectory $target
            }

            if ($exeReady) {
                Start-Sleep -Seconds 2
                try {
                    if ([System.IO.Directory]::Exists($stageRoot)) {
                        [System.IO.Directory]::Delete($stageRoot, $true)
                    }
                } catch {}
            }
            """;

        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
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
