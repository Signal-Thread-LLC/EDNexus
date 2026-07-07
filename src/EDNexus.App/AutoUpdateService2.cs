using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EDNexus.App.Services
{
    // Improved fallback updater: tries API, then HTML page parsing when API returns 404.
    public sealed record AutoUpdateResult(bool Found, string Message, string? Path, bool Verified);

    public static class AutoUpdateService2
    {
        private static readonly HttpClient Http = new HttpClient();
        public static event Action<string>? UpdateDownloaded;
        public static string? LastDownloadedPath;
        public static bool HasUpdate => !string.IsNullOrEmpty(LastDownloadedPath);

        /// <summary>Check for updates and return a structured result. Also raises UpdateDownloaded when an asset is downloaded.</summary>
        public static async Task<AutoUpdateResult> CheckForUpdatesAsync()
        {
            try
            {
                Http.DefaultRequestHeaders.UserAgent.ParseAdd("EDNexus-Updater");
                var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
                if (!string.IsNullOrWhiteSpace(token))
                {
                    try { Http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", token); } catch { }
                }
                Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github.v3+json");

                var repo = "Signal-Thread-LLC/EDNexus";
                var apiUrl = $"https://api.github.com/repos/{repo}/releases/latest";
                var resp = await Http.GetAsync(apiUrl).ConfigureAwait(false);

                System.Diagnostics.Trace.TraceInformation($"AutoUpdate: API response { (int)resp.StatusCode } {resp.StatusCode}");

                // If the API failed for a transient reason, try once more before falling back.
                if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    System.Diagnostics.Trace.TraceInformation("AutoUpdate: non-404 API response, retrying after 1s...");
                    await Task.Delay(1000).ConfigureAwait(false);
                    var resp2 = await Http.GetAsync(apiUrl).ConfigureAwait(false);
                    System.Diagnostics.Trace.TraceInformation($"AutoUpdate: retry response { (int)resp2.StatusCode } {resp2.StatusCode}");
                    if (resp2.IsSuccessStatusCode)
                    {
                        try { resp.Dispose(); } catch { }
                        resp = resp2; // use the successful response
                    }
                    else
                    {
                        // If still failing and it's a 404, fall through to the 404 handling below. Otherwise log and abort.
                        if (resp2.StatusCode != System.Net.HttpStatusCode.NotFound)
                        {
                            System.Diagnostics.Trace.TraceWarning($"AutoUpdate: API failed with {resp2.StatusCode}; aborting auto-update.");
                            try { resp2.Dispose(); } catch { }
                            return new AutoUpdateResult(false, $"API failed: {resp2.StatusCode}", null, false);
                        }
                        // else continue with resp2 (404) so HTML fallback executes
                        try { resp.Dispose(); } catch { }
                        resp = resp2;
                    }
                }

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    System.Diagnostics.Trace.TraceWarning("AutoUpdate: API returned 404 — falling back to HTML parsing of the releases page.");
                    // HTML fallback
                    var pageUrl = $"https://github.com/{repo}/releases/latest";
                    using var pageResp = await Http.GetAsync(pageUrl).ConfigureAwait(false);
                    System.Diagnostics.Trace.TraceInformation($"AutoUpdate: HTML page response { (int)pageResp.StatusCode } {pageResp.StatusCode}");
                    if (!pageResp.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Trace.TraceWarning($"AutoUpdate HTML fallback failed: {pageResp.StatusCode}");
                        return new AutoUpdateResult(false, $"HTML fallback failed: {pageResp.StatusCode}", null, false);
                    }

                    var html = await pageResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    System.Diagnostics.Trace.TraceInformation($"AutoUpdate: fetched releases page, length={html.Length}");
                    var snippet = html.Length > 1000 ? html.Substring(0, 1000) : html;
                    System.Diagnostics.Trace.TraceInformation($"AutoUpdate: page snippet:\n{snippet}");

                    // Manual parse for asset hrefs to avoid brittle regex escaping issues.
                    var marker = $"/{repo}/releases/download/";
                    int pos = 0;
                    while ((pos = html.IndexOf(marker, pos, StringComparison.OrdinalIgnoreCase)) >= 0)
                    {
                        // find surrounding quotes
                        int q1 = html.LastIndexOf('"', pos);
                        if (q1 < 0) { pos += marker.Length; continue; }
                        int q2 = html.IndexOf('"', pos);
                        if (q2 < 0) break;
                        var href = html.Substring(q1 + 1, q2 - q1 - 1);
                        if (!href.Contains(marker, StringComparison.OrdinalIgnoreCase)) { pos = q2; continue; }

                        var assetUrl = "https://github.com" + href;
                        var fileName = Path.GetFileName(href);
                        if (!IsMatchPlatform(fileName)) { pos = q2; continue; }

                        var dest = Path.Combine(Path.GetTempPath(), fileName ?? "EDNexus-update");
                        using var dl = await Http.GetAsync(assetUrl).ConfigureAwait(false);
                        if (!dl.IsSuccessStatusCode)
                        {
                            System.Diagnostics.Trace.TraceWarning($"AutoUpdate download failed ({dl.StatusCode}) for {assetUrl}");
                            pos = q2; continue;
                        }

                        using (var contentStream = await dl.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var fs = File.Create(dest))
                        {
                            await contentStream.CopyToAsync(fs).ConfigureAwait(false);
                        }

                        System.Diagnostics.Trace.TraceInformation($"EDNexus update downloaded to {dest}");
                        LastDownloadedPath = dest;

                            // Try to find a checksum asset nearby (best-effort): look for fileName+.sha256 or .sha256sum in the HTML.
                            bool verified = false;
                            try
                            {
                                var checksumCandidates = new[] { fileName + ".sha256", fileName + ".sha256sum", fileName + ".sha256.txt" };
                                foreach (var cand in checksumCandidates)
                                {
                                    var candMarker = $"/{repo}/releases/download/" + cand;
                                    var candPos = html.IndexOf(candMarker, StringComparison.OrdinalIgnoreCase);
                                    if (candPos >= 0)
                                    {
                                        int cq1 = html.LastIndexOf('"', candPos);
                                        int cq2 = html.IndexOf('"', candPos);
                                        if (cq1 >= 0 && cq2 > cq1)
                                        {
                                            var chref = html.Substring(cq1 + 1, cq2 - cq1 - 1);
                                            var checksumUrl = "https://github.com" + chref;
                                            using var cdl = await Http.GetAsync(checksumUrl).ConfigureAwait(false);
                                            if (cdl.IsSuccessStatusCode)
                                            {
                                                var sumText = await cdl.Content.ReadAsStringAsync().ConfigureAwait(false);
                                                var expected = sumText.Trim().Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                                                var actual = EDNexus.Core.Settings.Hashing.ComputeSha256Hex(dest);
                                                verified = string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }

                            UpdateDownloaded?.Invoke(dest);
                            return new AutoUpdateResult(true, verified ? "Downloaded and verified" : "Downloaded (no checksum or verification failed)", dest, verified);
                    }

                    System.Diagnostics.Trace.TraceWarning("AutoUpdate: no matching assets found on the releases page.");
                    return new AutoUpdateResult(false, "No matching assets found on releases page", null, false);
                }

                resp.EnsureSuccessStatusCode();
                using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                var root = doc.RootElement;
                if (!root.TryGetProperty("assets", out var assets) || assets.GetArrayLength() == 0)
                    return new AutoUpdateResult(false, "No assets found in release", null, false);

                foreach (var asset in assets.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out var nameEl)) continue;
                    var name = nameEl.GetString() ?? string.Empty;
                    if (!asset.TryGetProperty("browser_download_url", out var urlEl)) continue;
                    var downloadUrl = urlEl.GetString() ?? string.Empty;
                    if (!IsMatchPlatform(name)) continue;

                    var fileName = Path.GetFileName(name);
                    var dest = Path.Combine(Path.GetTempPath(), fileName ?? "EDNexus-update");

                    using var dl = await Http.GetAsync(downloadUrl).ConfigureAwait(false);
                    dl.EnsureSuccessStatusCode();

                    using (var contentStream = await dl.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var fs = File.Create(dest))
                    {
                        await contentStream.CopyToAsync(fs).ConfigureAwait(false);
                    }

                    System.Diagnostics.Trace.TraceInformation($"EDNexus update downloaded to {dest}");
                    LastDownloadedPath = dest;

                    // Try to verify against any checksum asset present in the release.
                    bool verified = false;
                    try
                    {
                        foreach (var a in assets.EnumerateArray())
                        {
                            if (!a.TryGetProperty("name", out var n2)) continue;
                            var name2 = n2.GetString() ?? string.Empty;
                            var lower = name2.ToLowerInvariant();
                            if (lower == (fileName + ".sha256") || lower == (fileName + ".sha256sum") || lower == (fileName + ".sha256.txt"))
                            {
                                if (a.TryGetProperty("browser_download_url", out var curl))
                                {
                                    var checksumUrl = curl.GetString() ?? string.Empty;
                                    using var cdl = await Http.GetAsync(checksumUrl).ConfigureAwait(false);
                                    if (cdl.IsSuccessStatusCode)
                                    {
                                        var sumText = await cdl.Content.ReadAsStringAsync().ConfigureAwait(false);
                                        var expected = sumText.Trim().Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                                        var actual = EDNexus.Core.Settings.Hashing.ComputeSha256Hex(dest);
                                        verified = string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    UpdateDownloaded?.Invoke(dest);
                    return new AutoUpdateResult(true, verified ? "Downloaded and verified" : "Downloaded (no checksum or verification failed)", dest, verified);
                }

                // No matching assets found in the release assets
                return new AutoUpdateResult(false, "No matching assets found in release", null, false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"AutoUpdate check failed: {ex}");
                return new AutoUpdateResult(false, $"Exception: {ex.Message}", null, false);
            }
        }

        private static bool IsMatchPlatform(string name)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return name.Contains("win", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("windows", StringComparison.OrdinalIgnoreCase);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return name.Contains("osx", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("mac", StringComparison.OrdinalIgnoreCase);

            return name.Contains("linux", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
        }

    }
}
