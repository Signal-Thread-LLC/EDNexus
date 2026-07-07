using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EDNexus.App.Services
{
    /// <summary>
    /// Simple GitHub Releases-based updater: checks the latest release, finds a platform-matching
    /// asset and downloads it to the user's temp folder. This is intentionally minimal — it only
    /// downloads the asset and logs the destination. More UI/installer integration can be added
    /// later.
    /// </summary>
    public static class AutoUpdateService
    {
        private static readonly HttpClient Http = new HttpClient();

        /// <summary>Last downloaded update path (if any).</summary>
        public static string? LastDownloadedPath;

        /// <summary>Raised when an update asset has been downloaded; argument is the full file path.</summary>
        public static event Action<string>? UpdateDownloaded;

        public static async Task CheckForUpdatesAsync()
        {
            try
            {
                Http.DefaultRequestHeaders.UserAgent.ParseAdd("EDNexus-Updater");
                // Prefer a GitHub token for private repos: set GITHUB_TOKEN or GH_TOKEN in the environment.
                var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
                if (!string.IsNullOrWhiteSpace(token))
                {
                    try
                    {
                        Http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", token);
                    }
                    catch { }
                }
                // Request the v3 API explicitly.
                Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github.v3+json");

                var repo = "Signal-Thread-LLC/EDNexus";
                var apiUrl = $"https://api.github.com/repos/{repo}/releases/latest";
                var resp = await Http.GetAsync(apiUrl).ConfigureAwait(false);

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    System.Diagnostics.Trace.TraceWarning("AutoUpdate: releases endpoint returned 404. Falling back to HTML page parsing (public releases may still be accessible via the website).");
                    // Try the HTML releases page as a fallback (public releases are visible via the web UI even when API access is restricted).
                    try
                    {
                        var pageUrl = $"https://github.com/{repo}/releases/latest";
                        using var pageResp = await Http.GetAsync(pageUrl).ConfigureAwait(false);
                        if (!pageResp.IsSuccessStatusCode)
                        {
                            System.Diagnostics.Trace.TraceWarning($"AutoUpdate HTML fallback failed: {pageResp.StatusCode}");
                            return;
                        }

                        var html = await pageResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        System.Diagnostics.Trace.TraceInformation($"AutoUpdate: fetched releases page, length={html.Length}");
                        var snippet = html.Length > 1000 ? html.Substring(0, 1000) : html;
                        System.Diagnostics.Trace.TraceInformation($"AutoUpdate: page snippet:\n{snippet}");

                        // Manual parse for asset hrefs
                        var marker = $"/{repo}/releases/download/";
                        int pos = 0;
                        while ((pos = html.IndexOf(marker, pos, StringComparison.OrdinalIgnoreCase)) >= 0)
                        {
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
                            UpdateDownloaded?.Invoke(dest);
                            return;
                        }

                        System.Diagnostics.Trace.TraceWarning("AutoUpdate: no matching assets found on the releases page.");
                        return;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.TraceWarning($"AutoUpdate HTML fallback failed: {ex}");
                        return;
                    }
                }

                resp.EnsureSuccessStatusCode();
                using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                var root = doc.RootElement;
                if (!root.TryGetProperty("assets", out var assets) || assets.GetArrayLength() == 0) return;

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
                    
                                        // Notify listeners (UI) that an update was downloaded.
                                        UpdateDownloaded?.Invoke(dest);
                                        return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"AutoUpdate check failed: {ex}");
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

            // Linux and other
            return name.Contains("linux", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
        }
    }
}
