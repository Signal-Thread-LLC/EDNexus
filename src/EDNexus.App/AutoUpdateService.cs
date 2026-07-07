using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
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

        /// <summary>Raised when an update asset has been downloaded; argument is the full file path.</summary>
        public static event Action<string>? UpdateDownloaded;

        public static async Task CheckForUpdatesAsync()
        {
            try
            {
                Http.DefaultRequestHeaders.UserAgent.ParseAdd("EDNexus-Updater");
                var url = "https://api.github.com/repos/Signal-Thread-LLC/EDNexus/releases/latest";
                using var resp = await Http.GetAsync(url).ConfigureAwait(false);
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
