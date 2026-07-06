using System.Text.Json;
using EDNexus.Core.Colonisation;
using EDNexus.Core.Journal;
using EDNexus.Core.State;

namespace EDNexus.Core.Market;

/// <summary>
/// Feature service that turns the <c>Market</c> event (mirrored from the game's <c>Market.json</c>
/// sidecar) into a live snapshot of the docked station's commodity board, and folds
/// <c>MarketBuy</c> / <c>MarketSell</c> transactions in between full snapshots so stock and demand
/// stay current. It owns its own derived state — it never mutates <see cref="CommanderState"/>,
/// only reads it to stamp the snapshot's location when the event omits it.
/// </summary>
public sealed class MarketTracker
{
    private readonly CommanderState? _state;
    private readonly object _gate = new();
    private MarketSnapshot? _current;

    /// <summary>Raised after any market/transaction event changes the tracked snapshot.</summary>
    public event Action? Changed;

    public MarketTracker(JournalEventBus bus, CommanderState? state = null)
    {
        _state = state;
        bus.Subscribe("Market", OnMarket);
        bus.Subscribe("MarketBuy", OnBuy);
        bus.Subscribe("MarketSell", OnSell);
    }

    /// <summary>The most recently seen station market, if any.</summary>
    public MarketSnapshot? Current
    {
        get { lock (_gate) return _current; }
    }

    private void OnMarket(JournalEntry e)
    {
        if (e.GetInt64("MarketID") is not long marketId) return;

        // The Market event sometimes arrives as a bare header and defers the board to Market.json;
        // only rebuild when the Items array is actually present so we don't wipe a good snapshot.
        if (!e.Raw.TryGetProperty("Items", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        var items = new List<MarketCommodity>();
        foreach (var item in arr.EnumerateArray())
        {
            var symbolRaw = ReadString(item, "Name");
            var localised = ReadString(item, "Name_Localised");
            var display = localised ?? symbolRaw ?? "Unknown";
            var symbol = CommodityName.Canonicalize(symbolRaw ?? localised);
            if (symbol.Length == 0) continue;

            items.Add(new MarketCommodity(
                Name: display,
                Symbol: symbol,
                Category: ReadString(item, "Category_Localised") ?? ReadString(item, "Category") ?? "",
                BuyPrice: ReadInt(item, "BuyPrice"),
                SellPrice: ReadInt(item, "SellPrice"),
                MeanPrice: ReadInt(item, "MeanPrice"),
                Stock: ReadInt(item, "Stock"),
                Demand: ReadInt(item, "Demand"),
                Rare: item.TryGetProperty("Rare", out var r) && r.ValueKind is JsonValueKind.True));
        }

        var snap = new MarketSnapshot
        {
            MarketId = marketId,
            StationName = e.GetString("StationName") ?? _state?.StationName,
            StarSystem = e.GetString("StarSystem") ?? _state?.StarSystem,
            Updated = e.Timestamp,
            Commodities = items,
        };

        lock (_gate) _current = snap;
        Changed?.Invoke();
    }

    private void OnBuy(JournalEntry e) => ApplyTransaction(e, buying: true);

    private void OnSell(JournalEntry e) => ApplyTransaction(e, buying: false);

    private void ApplyTransaction(JournalEntry e, bool buying)
    {
        if (e.GetInt64("MarketID") is not long marketId) return;
        var symbol = CommodityName.Canonicalize(e.GetString("Type") ?? e.GetString("Type_Localised"));
        if (symbol.Length == 0) return;
        var count = (int)(e.GetInt64("Count") ?? 0);
        if (count <= 0) return;

        lock (_gate)
        {
            // A transaction only makes sense against the market it happened at; if we've since moved
            // on (or never loaded that board) there is nothing to reconcile — the next Market snapshot
            // is authoritative and will re-establish stock/demand anyway.
            if (_current is null || _current.MarketId != marketId) return;

            var updated = _current.Commodities.Select(c =>
            {
                if (c.Symbol != symbol) return c;
                return buying
                    ? c with { Stock = Math.Max(0, c.Stock - count) }   // bought from the station: stock drops
                    : c with { Demand = Math.Max(0, c.Demand - count) }; // sold to the station: demand drops
            }).ToList();

            _current = new MarketSnapshot
            {
                MarketId = _current.MarketId,
                StationName = _current.StationName,
                StarSystem = _current.StarSystem,
                Updated = e.Timestamp,
                Commodities = updated,
            };
        }
        Changed?.Invoke();
    }

    private static string? ReadString(JsonElement item, string prop)
        => item.TryGetProperty(prop, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static int ReadInt(JsonElement item, string prop)
        => item.TryGetProperty(prop, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v) ? v : 0;
}
