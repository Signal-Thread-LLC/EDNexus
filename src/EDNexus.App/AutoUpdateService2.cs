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
    public static class AutoUpdateService2
    {
        private static readonly HttpClient Http = new HttpClient();
        public static event Action<string>? UpdateDownloaded;

        public static async Task CheckForUpdatesAsync()
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

                var apiUrl = "https://api.github.com/repos/Signal-Thread-LLC/EDNexus/releases/latest";
                using var resp = await Http.GetAsync(apiUrl).ConfigureAwait(false);

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    System.Diagnostics.Trace.TraceWarning("AutoUpdate: API returned 404 — falling back to HTML parsing of the releases page.");
                    // HTML fallback
                    var pageUrl = "https://github.com/Signal-Thread-LLC/EDNexus/releases/latest";
                    using var pageResp = await Http.GetAsync(pageUrl).ConfigureAwait(false);
                    if (!pageResp.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Trace.TraceWarning($"AutoUpdate HTML fallback failed: {pageResp.StatusCode}");
                        return;
                    }

                    var html = await pageResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var pattern = "href\\s*=\\s*\\\"(?<h>/Signal-Thread-LLC/EDNexus/releases/download/[^\"\"]+)\\\"";
                    var matches = Regex.Matches(html, pattern);
                    foreach (Match m in matches)
                    {
                        var href = m.Groups["h"].Value;
                        var assetUrl = "https://github.com" + href;
                        var fileName = Path.GetFileName(href);
                        if (!IsMatchPlatform(fileName)) continue;

                        var dest = Path.Combine(Path.GetTempPath(), fileName ?? "EDNexus-update");
                        using var dl = await Http.GetAsync(assetUrl).ConfigureAwait(false);
                        if (!dl.IsSuccessStatusCode)
                        {
                            System.Diagnostics.Trace.TraceWarning($"AutoUpdate download failed ({dl.StatusCode}) for {assetUrl}");
                            continue;
                        }

                        using (var contentStream = await dl.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var fs = File.Create(dest))
                        {
                            await contentStream.CopyToAsync(fs).ConfigureAwait(false);
                        }

                        System.Diagnostics.Trace.TraceInformation($"EDNexus update downloaded to {dest}");
                        UpdateDownloaded?.Invoke(dest);
                        return;
                    }

                    System.Diagnostics.Trace.TraceWarning("AutoUpdate: no matching assets found on the releases page.");
                    return;
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

            return name.Contains("linux", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
        }
    }
}
