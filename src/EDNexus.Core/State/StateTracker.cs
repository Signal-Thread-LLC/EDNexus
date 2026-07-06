using System.Collections.Concurrent;
using System.Text.Json;
using EDNexus.Core.Journal;

namespace EDNexus.Core.State;

/// <summary>
/// Wires journal events to <see cref="CommanderState"/>. This is the one place that knows
/// how individual events mutate the live picture; feature modules read state instead of
/// re-parsing events themselves.
/// </summary>
public sealed class StateTracker
{
    private readonly CommanderState _state;

    public StateTracker(JournalEventBus bus, CommanderState state)
    {
        _state = state;

        bus.Subscribe("Commander", OnCommander);
        bus.Subscribe("LoadGame", OnLoadGame);
        bus.Subscribe("Loadout", OnLoadout);
        bus.Subscribe("Location", OnLocation);
        bus.Subscribe("FSDJump", OnJump);
        bus.Subscribe("CarrierJump", OnJump);
        bus.Subscribe("Docked", OnDocked);
        bus.Subscribe("Undocked", OnUndocked);
        bus.Subscribe("SupercruiseExit", OnSupercruiseExit);
        bus.Subscribe("Status", OnStatus);
        bus.Subscribe("Cargo", OnCargo);
        bus.Subscribe("Materials", OnMaterials);
        bus.Subscribe("MaterialCollected", OnMaterialCollected);

        bus.SubscribeAny(e =>
        {
            if (e.Timestamp != default) _state.LastUpdated = e.Timestamp;
        });
    }

    private void OnCommander(JournalEntry e) => _state.Name = e.GetString("Name") ?? _state.Name;

    private void OnLoadGame(JournalEntry e)
    {
        _state.Name = e.GetString("Commander") ?? _state.Name;
        _state.Ship = e.GetLocalised("Ship") ?? _state.Ship;
        _state.ShipName = e.GetString("ShipName") ?? _state.ShipName;
        _state.ShipIdent = e.GetString("ShipIdent") ?? _state.ShipIdent;
        if (e.GetInt64("Credits") is long credits) _state.Balance = credits;
    }

    private void OnLoadout(JournalEntry e)
    {
        _state.Ship = e.GetLocalised("Ship") ?? _state.Ship;
        _state.ShipName = e.GetString("ShipName") ?? _state.ShipName;
        _state.ShipIdent = e.GetString("ShipIdent") ?? _state.ShipIdent;
        if (e.Raw.TryGetProperty("FuelCapacity", out var fc)
            && fc.TryGetProperty("Main", out var main) && main.TryGetDouble(out var cap))
            _state.FuelCapacity = cap;
    }

    private void OnLocation(JournalEntry e)
    {
        _state.StarSystem = e.GetString("StarSystem") ?? _state.StarSystem;
        _state.Body = e.GetString("Body") ?? _state.Body;
        _state.Docked = e.GetBool("Docked") ?? _state.Docked;
        _state.StationName = e.GetString("StationName");
    }

    private void OnJump(JournalEntry e)
    {
        _state.StarSystem = e.GetString("StarSystem") ?? _state.StarSystem;
        _state.Body = e.GetString("Body") ?? e.GetString("StarSystem");
        _state.Docked = false;
        _state.StationName = null;
    }

    private void OnDocked(JournalEntry e)
    {
        _state.Docked = true;
        _state.StationName = e.GetString("StationName");
    }

    private void OnUndocked(JournalEntry e)
    {
        _state.Docked = false;
        _state.StationName = null;
    }

    private void OnSupercruiseExit(JournalEntry e) => _state.Body = e.GetString("Body") ?? _state.Body;

    private void OnStatus(JournalEntry e)
    {
        if (e.Raw.TryGetProperty("Fuel", out var fuel)
            && fuel.TryGetProperty("FuelMain", out var fm) && fm.TryGetDouble(out var main))
            _state.FuelMain = main;
        if (e.GetDouble("Cargo") is double cargo) _state.CargoTons = cargo;
        if (e.GetInt64("Balance") is long balance) _state.Balance = balance;
    }

    private void OnCargo(JournalEntry e)
    {
        // The Cargo event sometimes omits Inventory and defers to Cargo.json; only rebuild
        // when the array is actually present so we don't wipe the hold to empty.
        if (!e.Raw.TryGetProperty("Inventory", out var inv) || inv.ValueKind != JsonValueKind.Array)
            return;

        _state.Cargo.Clear();
        foreach (var item in inv.EnumerateArray())
        {
            var name = ReadLocalisedName(item);
            var count = item.TryGetProperty("Count", out var c) && c.TryGetInt32(out var n) ? n : 0;
            if (name is not null) _state.Cargo[name] = count;
        }
        _state.RaiseCargoChanged();
    }

    private void OnMaterials(JournalEntry e)
    {
        LoadCategory(e, "Raw", _state.Materials.Raw);
        LoadCategory(e, "Manufactured", _state.Materials.Manufactured);
        LoadCategory(e, "Encoded", _state.Materials.Encoded);
        _state.RaiseMaterialsChanged();
    }

    private void OnMaterialCollected(JournalEntry e)
    {
        var category = e.GetString("Category");
        var name = e.GetString("Name");
        if (name is null || category is null) return;

        var count = (int)(e.GetInt64("Count") ?? 1);
        TargetFor(category).AddOrUpdate(name, count, (_, v) => v + count);
        _state.RaiseMaterialsChanged();
    }

    private ConcurrentDictionary<string, int> TargetFor(string category) => category.ToLowerInvariant() switch
    {
        "raw" => _state.Materials.Raw,
        "manufactured" => _state.Materials.Manufactured,
        _ => _state.Materials.Encoded,
    };

    private static void LoadCategory(JournalEntry e, string prop, ConcurrentDictionary<string, int> target)
    {
        if (!e.Raw.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        target.Clear();
        foreach (var item in arr.EnumerateArray())
        {
            var name = item.TryGetProperty("Name", out var n) ? n.GetString() : null;
            var count = item.TryGetProperty("Count", out var c) && c.TryGetInt32(out var v) ? v : 0;
            if (name is not null) target[name] = count;
        }
    }

    private static string? ReadLocalisedName(JsonElement item)
    {
        if (item.TryGetProperty("Name_Localised", out var loc) && loc.ValueKind == JsonValueKind.String)
            return loc.GetString();
        return item.TryGetProperty("Name", out var n) ? n.GetString() : null;
    }
}
