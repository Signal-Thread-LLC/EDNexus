using System.Text.Json;

namespace EDNexus.Core.News;

/// <summary>
/// Tracks which news articles a commander has already opened, so the reader can mark headlines
/// unread / new-since-last-open. Backed by a small on-disk set of remembered article ids — deliberately
/// not <see cref="Trade.IResponseCache"/>, which is about avoiding refetches, not read state, and is
/// keyed by feed contents rather than surviving indefinitely across refreshes.
/// </summary>
/// <remarks>
/// Persisted so the "new" badge is honest across restarts (a headline read yesterday should not come
/// back as new today just because the process restarted). The remembered set is capped so a commander
/// who never closes the app doesn't grow the file forever — Galnet publishes at most a few dozen
/// articles a week, so a few hundred ids comfortably covers "have I seen this one before".
/// </remarks>
public sealed class NewsReadTracker
{
    private const int MaxRemembered = 500;

    private readonly string _path;
    private readonly HashSet<string> _seen;

    public NewsReadTracker(string? path = null)
    {
        _path = path ?? DefaultPath();
        _seen = Load(_path);
    }

    public static string DefaultPath() => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(Settings.SettingsStore.DefaultPath())!, "cache", "galnet", "read.json");

    /// <summary>True when <paramref name="articleId"/> has never been marked read.</summary>
    public bool IsUnread(string articleId) => !string.IsNullOrEmpty(articleId) && !_seen.Contains(articleId);

    /// <summary>Mark one article as read (opened in the reader).</summary>
    public void MarkRead(string articleId)
    {
        if (string.IsNullOrEmpty(articleId)) return;
        if (!_seen.Add(articleId)) return;
        Save();
    }

    /// <summary>Mark every id in <paramref name="articleIds"/> as read in one write — used by "mark all read".</summary>
    public void MarkAllRead(IEnumerable<string> articleIds)
    {
        var changed = false;
        foreach (var id in articleIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            changed |= _seen.Add(id);
        }
        if (changed) Save();
    }

    private static HashSet<string> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new HashSet<string>();
            var ids = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
            return ids is null ? new HashSet<string>() : new HashSet<string>(ids);
        }
        catch (JsonException)
        {
            return new HashSet<string>(); // a corrupt read-state file just means everything looks new again.
        }
        catch (IOException)
        {
            return new HashSet<string>();
        }
    }

    private void Save()
    {
        try
        {
            // Trim from the front (insertion order) rather than growing forever. HashSet doesn't
            // preserve order strictly, but this is a soft cap, not a precise "oldest wins" eviction.
            if (_seen.Count > MaxRemembered)
            {
                var keep = _seen.Skip(_seen.Count - MaxRemembered).ToList();
                _seen.Clear();
                foreach (var id in keep) _seen.Add(id);
            }

            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(_seen));
        }
        catch (IOException)
        {
            // Best-effort; a failed save must not crash the app — worst case a headline shows as new again.
        }
    }
}
