using System.Text.Json;
using EDNexus.Core.Journal;
using EDNexus.Core.State;

namespace EDNexus.Core.Colonisation;

/// <summary>
/// Feature service that turns <c>ColonisationConstructionDepot</c> and
/// <c>ColonisationContribution</c> events into a live per-site tracker of required / provided /
/// remaining commodities. It owns its own derived state — it never mutates
/// <see cref="CommanderState"/>, only reads it to stamp the site's location.
/// </summary>
public sealed class ColonisationTracker
{
    private readonly CommanderState? _state;
    private readonly object _gate = new();
    private readonly Dictionary<long, ColonisationSite> _sites = new();
    private long? _activeMarketId;

    /// <summary>Raised after any depot/contribution event changes the tracked picture.</summary>
    public event Action? Changed;

    public ColonisationTracker(JournalEventBus bus, CommanderState? state = null)
    {
        _state = state;
        bus.Subscribe("ColonisationConstructionDepot", OnDepot);
        bus.Subscribe("ColonisationContribution", OnContribution);
    }

    /// <summary>All known construction sites, most recently updated first.</summary>
    public IReadOnlyList<ColonisationSite> Sites
    {
        get { lock (_gate) return _sites.Values.OrderByDescending(s => s.Updated).ToList(); }
    }

    /// <summary>The site whose depot/contribution was seen most recently, if any.</summary>
    public ColonisationSite? ActiveSite
    {
        get
        {
            lock (_gate)
                return _activeMarketId is long id && _sites.TryGetValue(id, out var s) ? s : null;
        }
    }

    private void OnDepot(JournalEntry e)
    {
        if (e.GetInt64("MarketID") is not long marketId) return;
        if (!e.Raw.TryGetProperty("ResourcesRequired", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        var resources = new List<ColonisationResource>();
        foreach (var item in arr.EnumerateArray())
        {
            var symbolRaw = ReadString(item, "Name");
            var localised = ReadString(item, "Name_Localised");
            var display = localised ?? symbolRaw ?? "Unknown";
            var symbol = CommodityName.Canonicalize(symbolRaw ?? localised);

            resources.Add(new ColonisationResource(
                Name: display,
                Symbol: symbol,
                Required: ReadInt(item, "RequiredAmount"),
                Provided: ReadInt(item, "ProvidedAmount"),
                Payment: ReadInt(item, "Payment")));
        }

        var site = new ColonisationSite
        {
            MarketId = marketId,
            StationName = _state?.StationName,
            StarSystem = _state?.StarSystem,
            Progress = e.GetDouble("ConstructionProgress") ?? 0,
            Complete = e.GetBool("ConstructionComplete") ?? false,
            Failed = e.GetBool("ConstructionFailed") ?? false,
            Updated = e.Timestamp,
            Resources = resources,
        };

        lock (_gate)
        {
            _sites[marketId] = site;
            _activeMarketId = marketId;
        }
        Changed?.Invoke();
    }

    private void OnContribution(JournalEntry e)
    {
        if (e.GetInt64("MarketID") is not long marketId) return;
        if (!e.Raw.TryGetProperty("Contributions", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        // Fold the deltas into a symbol → tons map keyed the same way as the depot resources.
        var deltas = new Dictionary<string, int>();
        foreach (var item in arr.EnumerateArray())
        {
            var symbol = CommodityName.Canonicalize(ReadString(item, "Name") ?? ReadString(item, "Name_Localised"));
            if (symbol.Length == 0) continue;
            var amount = ReadInt(item, "Amount");
            deltas[symbol] = deltas.TryGetValue(symbol, out var v) ? v + amount : amount;
        }
        if (deltas.Count == 0) return;

        lock (_gate)
        {
            // A depot snapshot is authoritative and usually follows a contribution within moments;
            // until then we apply the delta so the shopping list updates live. Unknown sites are
            // skipped rather than invented — the next depot snapshot will establish them.
            if (!_sites.TryGetValue(marketId, out var site)) return;

            var updated = site.Resources.Select(r =>
                deltas.TryGetValue(r.Symbol, out var delta)
                    ? r with { Provided = Math.Min(r.Required, r.Provided + delta) }
                    : r).ToList();

            _sites[marketId] = new ColonisationSite
            {
                MarketId = site.MarketId,
                StationName = site.StationName,
                StarSystem = site.StarSystem,
                Progress = site.Progress,
                Complete = site.Complete,
                Failed = site.Failed,
                Updated = e.Timestamp,
                Resources = updated,
            };
            _activeMarketId = marketId;
        }
        Changed?.Invoke();
    }

    private static string? ReadString(JsonElement item, string prop)
        => item.TryGetProperty(prop, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static int ReadInt(JsonElement item, string prop)
        => item.TryGetProperty(prop, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v) ? v : 0;
}
