using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using HaloDeck.Core.Models;

namespace HaloDeck.App.Services;

/// <summary>
/// Scrapes and downloads trainers from https://flingtrainer.com/ (FLiNG / 风灵月影).
/// The site has no public API: search is WordPress (?s=), each trainer page lists standalone
/// versions as /downloads/{token},, links whose `title` carries the version, and downloading
/// requires following the redirect chain with a cookie jar + a Referer header.
/// </summary>
public sealed class FlingTrainerService
{
    public const string BaseUrl = "https://flingtrainer.com/";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private static readonly Regex SearchLinkRegex = new(
        @"<a\s+[^>]*href=""(?<url>https://flingtrainer\.com/trainer/[^""]+/)""[^>]*rel=""bookmark""[^>]*>(?<title>[^<]+)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DownloadLinkRegex = new(
        @"<a\s+[^>]*href=""(?<url>https://flingtrainer\.com/downloads/[^""]+)""[^>]*title=""(?<title>[^""]+)""[^>]*class=""[^""]*attachment-link[^""]*""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;

    public FlingTrainerService()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public async Task<List<FlingTrainerSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        string html = await GetStringAsync($"{BaseUrl}?s={Uri.EscapeDataString(query)}", ct).ConfigureAwait(false);

        var results = new List<FlingTrainerSearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in SearchLinkRegex.Matches(html))
        {
            string resultUrl = m.Groups["url"].Value;
            if (!seen.Add(resultUrl))
            {
                continue;
            }
            results.Add(new FlingTrainerSearchResult
            {
                Title = WebUtility.HtmlDecode(m.Groups["title"].Value.Trim()),
                Url = resultUrl
            });
        }
        return results;
    }

    public async Task<List<FlingTrainerDownload>> GetDownloadsAsync(string trainerUrl, CancellationToken ct = default)
    {
        string html = await GetStringAsync(trainerUrl, ct).ConfigureAwait(false);

        var list = new List<FlingTrainerDownload>();
        int idx = 0;
        foreach (Match m in DownloadLinkRegex.Matches(html))
        {
            list.Add(new FlingTrainerDownload
            {
                Version = WebUtility.HtmlDecode(m.Groups["title"].Value.Trim()),
                DownloadUrl = m.Groups["url"].Value,
                IsLatest = idx == 0
            });
            idx++;
        }
        return list;
    }

    /// <summary>
    /// Downloads the trainer executable to <paramref name="destPath"/>. The /downloads/ link redirects
    /// through download-trainer.php to the actual .exe; each hop needs the Referer of the previous page
    /// plus the cookies issued along the way, so we follow redirects manually.
    /// </summary>
    public async Task DownloadAsync(
        string downloadUrl,
        string referer,
        string destPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        string currentUrl = downloadUrl;
        string currentReferer = referer;

        for (int hop = 0; hop < 8; hop++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, currentUrl);
            req.Headers.Referrer = new Uri(currentReferer);

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            var location = resp.Headers.Location;
            if (location is not null && (int)resp.StatusCode is >= 300 and < 400)
            {
                string next = location.IsAbsoluteUri
                    ? location.AbsoluteUri
                    : new Uri(new Uri(currentUrl), location).AbsoluteUri;
                currentReferer = currentUrl;
                currentUrl = next;
                continue;
            }

            resp.EnsureSuccessStatusCode();

            long? length = resp.Content.Headers.ContentLength;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                total += read;
                if (length is > 0)
                {
                    progress?.Report(Math.Clamp((double)total / length.Value * 100.0, 0.0, 100.0));
                }
            }
            await dst.FlushAsync(ct).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException("下载重定向次数过多，已中止");
    }

    public static void Launch(string exePath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            Verb = "runas"
        });
    }

    public static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        name = name.Trim();
        return string.IsNullOrWhiteSpace(name) ? "trainer" : name;
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
