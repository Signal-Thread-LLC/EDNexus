using System.Text.Json.Nodes;

namespace EDNexus.Core.Dev;

/// <summary>
/// Base class every feature/card inherits to expose itself in developer mode: it produces a batch
/// of random-but-valid journal events which <see cref="DeveloperMode"/> publishes through the real
/// event bus. Because the output is genuine event JSON, it flows the normal path
/// (bus → <c>StateTracker</c> / feature services → UI) and exercises the real parsers — nothing
/// bypasses the one-writer rule.
/// </summary>
public abstract class JournalSampleSource
{
    /// <summary>Stable key matching the card in the UI (e.g. "location", "colonisation").</summary>
    public abstract string CardKey { get; }

    /// <summary>Human-facing label, for logging and dev tooltips.</summary>
    public abstract string DisplayName { get; }

    /// <summary>Produce a fresh batch of journal event lines (JSON) describing a random state.</summary>
    public abstract IReadOnlyList<string> Sample(Random rng);

    /// <summary>Pick a random element from a list.</summary>
    protected static T Pick<T>(Random rng, IReadOnlyList<T> items) => items[rng.Next(items.Count)];

    /// <summary>
    /// Build one event line: seeds the envelope (<c>event</c> + a live <c>timestamp</c>) and lets the
    /// caller fill in the payload fields.
    /// </summary>
    protected static string Event(string name, Action<JsonObject> build)
    {
        var o = new JsonObject
        {
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("o"),
            ["event"] = name,
        };
        build(o);
        return o.ToJsonString();
    }
}
