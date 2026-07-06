using EDNexus.Core.Colonisation;
using EDNexus.Core.Journal;
using EDNexus.Core.State;

// EDNexus.Cli — a headless harness for the journal engine.
//   (no args)   resolve the journal folder, replay to warm state, then watch live.
//   --once      replay + print final state, then exit (handy for validation / CI).
//   --dir <p>   use a specific journal directory instead of auto-detecting.

string? dir = null;
for (var i = 0; i < args.Length; i++)
    if (args[i] == "--dir" && i + 1 < args.Length) dir = args[++i];

dir ??= JournalPaths.Resolve();
if (dir is null)
{
    Console.Error.WriteLine("Could not locate the Elite Dangerous journal folder.");
    Console.Error.WriteLine($"Set {JournalPaths.OverrideEnvVar} or pass --dir <path>.");
    return 1;
}

Console.WriteLine($"Journal directory: {dir}");

var bus = new JournalEventBus();
var state = new CommanderState();
_ = new StateTracker(bus, state);
var colonisation = new ColonisationTracker(bus, state);

var liveCounts = new SortedDictionary<string, int>();
bus.SubscribeAny(e =>
{
    if (e.IsHistorical) return;
    liveCounts.TryGetValue(e.Event, out var c);
    liveCounts[e.Event] = c + 1;
});
bus.HandlerError += (e, ex) => Console.Error.WriteLine($"  [handler error on {e.Event}] {ex.Message}");

var watcher = new JournalWatcher(dir, bus);
Console.WriteLine("Replaying latest journal to warm up state...\n");
watcher.Replay();
PrintState(state);
PrintColonisation(colonisation, state);

if (args.Contains("--once"))
    return 0;

Console.WriteLine("\nWatching live. Press Ctrl+C to stop.\n");
bus.SubscribeAny(e =>
{
    if (!e.IsHistorical) Console.WriteLine($"  {e.Timestamp.LocalDateTime:HH:mm:ss}  {e.Event}");
});

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, ev) => { ev.Cancel = true; cts.Cancel(); };
await watcher.RunAsync(cts.Token);

Console.WriteLine();
PrintState(state);
PrintColonisation(colonisation, state);
if (liveCounts.Count > 0)
{
    Console.WriteLine("\nLive events this session:");
    foreach (var kv in liveCounts.OrderByDescending(k => k.Value))
        Console.WriteLine($"  {kv.Value,4}  {kv.Key}");
}
return 0;

static void PrintState(CommanderState s)
{
    string cr(long v) => v.ToString("N0") + " cr";
    Console.WriteLine("======== Commander ========");
    Console.WriteLine($"  CMDR      : {s.Name ?? "(unknown)"}");
    Console.WriteLine($"  Ship      : {s.Ship}{(s.ShipName is null ? "" : $" \"{s.ShipName}\"")}{(s.ShipIdent is null ? "" : $" [{s.ShipIdent}]")}");
    Console.WriteLine($"  Balance   : {cr(s.Balance)}");
    Console.WriteLine($"  System    : {s.StarSystem ?? "(unknown)"}");
    Console.WriteLine($"  Body      : {s.Body}");
    Console.WriteLine($"  Location  : {(s.Docked ? $"docked at {s.StationName}" : "in flight")}");
    Console.WriteLine($"  Fuel      : {s.FuelMain:0.0}{(s.FuelCapacity > 0 ? $" / {s.FuelCapacity:0.0}" : "")} t");
    Console.WriteLine($"  Cargo     : {s.CargoTons:0} t ({s.Cargo.Count} commodities)");

    var m = s.Materials;
    Console.WriteLine($"  Materials : {m.TotalCount} total  (raw {m.Raw.Values.Sum()}, mfd {m.Manufactured.Values.Sum()}, enc {m.Encoded.Values.Sum()})");
    Console.WriteLine($"  Updated   : {s.LastUpdated.LocalDateTime:yyyy-MM-dd HH:mm:ss}");

    if (!s.Cargo.IsEmpty)
    {
        Console.WriteLine("  --- hold ---");
        foreach (var kv in s.Cargo.OrderByDescending(k => k.Value))
            Console.WriteLine($"      {kv.Value,4}  {kv.Key}");
    }
}

static void PrintColonisation(ColonisationTracker tracker, CommanderState s)
{
    var site = tracker.ActiveSite;
    if (site is null) return;

    Console.WriteLine("\n======== Colonisation ========");
    var where = site.StationName ?? site.StarSystem ?? $"market {site.MarketId}";
    var status = site.Complete ? "COMPLETE" : site.Failed ? "FAILED" : $"{site.Progress * 100:0.0}%";
    Console.WriteLine($"  Site      : {where}");
    Console.WriteLine($"  Progress  : {status}  ({site.CompletedCount}/{site.Resources.Count} commodities, {site.TotalRemaining:N0} t remaining)");

    var list = site.BuildShoppingList(s.Cargo);
    if (list.Count == 0)
    {
        Console.WriteLine("  Nothing outstanding — depot fully supplied.");
        return;
    }

    Console.WriteLine("  --- shopping list (worst shortfall first) ---");
    Console.WriteLine($"      {"remaining",9}  {"in hold",8}  {"to buy",8}  commodity");
    foreach (var item in list)
    {
        var hold = item.InHold > 0 ? item.Carrying.ToString("N0") : "-";
        var flag = item.CoveredByHold ? " ✓ carrying enough" : "";
        Console.WriteLine($"      {item.Remaining,9:N0}  {hold,8}  {item.StillNeeded,8:N0}  {item.Name}{flag}");
    }
}
