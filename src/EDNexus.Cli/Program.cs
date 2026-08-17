using EDNexus.Core.Colonisation;
using EDNexus.Core.Engineering;
using EDNexus.Core.Exobio;
using EDNexus.Core.Journal;
using EDNexus.Core.Market;
using EDNexus.Core.Materials;
using EDNexus.Core.Ranks;
using EDNexus.Core.State;

// EDNexus.Cli — a headless harness for the journal engine.
//   (no args)   resolve the journal folder, replay to warm state, then watch live.
//   --once      replay + print final state, then exit (handy for validation / CI).
//   --dir <p>   use a specific journal directory instead of auto-detecting.
//   --plan <blueprint-id> [grade] [rolls]
//               cost an engineering roll against the live inventory and print the shopping list.
//               With no blueprint id, lists what can be planned.

string? dir = null;
string? planId = null;
var planGrade = 5;
var planRolls = 1;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--dir" && i + 1 < args.Length) dir = args[++i];
    else if (args[i] == "--plan")
    {
        planId = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "";
        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var g)) { planGrade = g; i++; }
        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var r)) { planRolls = r; i++; }
    }
}

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
var market = new MarketTracker(bus, state);
var exobio = new ExobiologyTracker(bus, state);
var engineering = new EngineeringTracker(bus);
var ranks = new RankTracker(bus);

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
PrintMarket(market, state);
PrintMaterials(state);
PrintExobiology(exobio);
PrintEngineers(engineering);
PrintRanks(ranks);
if (planId is not null) PrintEngineeringPlan(planId, planGrade, planRolls, state);

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
PrintMarket(market, state);
PrintMaterials(state);
PrintExobiology(exobio);
PrintEngineers(engineering);
PrintRanks(ranks);
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
    Console.WriteLine($"  Location  : {(s.Docked ? $"docked at {s.StationDisplayName}" : "in flight")}");
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

static void PrintMarket(MarketTracker tracker, CommanderState s)
{
    var snap = tracker.Current;
    if (snap is null) return;

    Console.WriteLine("\n======== Market ========");
    var where = snap.StationName ?? snap.StarSystem ?? $"market {snap.MarketId}";
    var system = snap.StarSystem is { Length: > 0 } sys && sys != snap.StationName ? $" ({sys})" : "";
    var sellable = snap.Commodities.Count(c => c.Sellable);
    Console.WriteLine($"  Station   : {where}{system}");
    Console.WriteLine($"  Board     : {snap.Commodities.Count} commodities · {sellable} the station buys");

    var valuation = snap.ValuateHold(s.Cargo);
    if (valuation.Count > 0)
    {
        Console.WriteLine($"  --- your hold, sold here ({snap.HoldValue(s.Cargo):N0} cr total) ---");
        Console.WriteLine($"      {"units",6}  {"unit cr",9}  {"total cr",12}  {"vs mean",8}  commodity");
        foreach (var i in valuation)
        {
            var vsMean = i.VsMean >= 0 ? $"+{i.VsMean:N0}" : i.VsMean.ToString("N0");
            Console.WriteLine($"      {i.Units,6:N0}  {i.UnitPrice,9:N0}  {i.Total,12:N0}  {vsMean,8}  {i.Name}");
        }
        return;
    }

    var topSells = snap.Sellable.Take(8).ToList();
    if (topSells.Count == 0) return;
    Console.WriteLine("  --- best sells here (station demand) ---");
    Console.WriteLine($"      {"sell cr",9}  {"vs mean",8}  {"demand",8}  commodity");
    foreach (var c in topSells)
    {
        var vsMean = c.SellVsMean >= 0 ? $"+{c.SellVsMean:N0}" : c.SellVsMean.ToString("N0");
        Console.WriteLine($"      {c.SellPrice,9:N0}  {vsMean,8}  {c.Demand,8:N0}  {c.Name}");
    }
}

static void PrintMaterials(CommanderState s)
{
    var views = MaterialInventory.All(s);
    if (views.All(v => v.TotalHeld == 0)) return;

    Console.WriteLine("\n======== Materials ========");
    foreach (var view in views)
        Console.WriteLine($"  {view.Category,-13}: {view.TotalHeld,6:N0} held across {view.Held.Count,3} materials"
                          + (view.FullCount > 0 ? $"  ({view.FullCount} at cap)" : ""));

    var capped = MaterialInventory.AtCap(s);
    if (capped.Count == 0) return;

    // At the cap the game bins further pickups, so this is the actionable list.
    Console.WriteLine("  --- at cap (trade these down) ---");
    Console.WriteLine($"      {"grade",5}  {"held",6}  material");
    foreach (var stock in capped)
        Console.WriteLine($"      {"G" + stock.Grade,5}  {stock.Held,6:N0}  {stock.Name}");
}

static void PrintExobiology(ExobiologyTracker tracker)
{
    var session = tracker.Session;
    var bodies = tracker.Bodies;
    if (bodies.Count == 0 && session.Pending.Count == 0 && session.SoldValue == 0) return;

    Console.WriteLine("\n======== Exobiology ========");

    if (tracker.CurrentBody is { } here)
    {
        var genera = here.Genera.Count > 0 ? string.Join(", ", here.Genera.Select(g => g.Name)) : "(not mapped)";
        Console.WriteLine($"  Here      : {here.BodyName} — {here.SignalCount} bio signal(s): {genera}");
        if (here.ValueRange is { } r)
            Console.WriteLine($"  Estimate  : {r.Min:N0} – {r.Max:N0} cr");
    }

    if (tracker.ActiveScan is { } scan)
        Console.WriteLine($"  Sampling  : {scan.SpeciesName} {scan.Progress} on {scan.BodyName}");

    Console.WriteLine($"  Pending   : {session.PendingValue:N0} cr across {session.Pending.Count} sample(s)");
    Console.WriteLine($"  Sold      : {session.SoldValue:N0} cr from {session.SoldCount} sample(s)"
                      + (session.SoldBonus > 0 ? $"  (incl. {session.SoldBonus:N0} cr first-logged bonus)" : ""));
    if (tracker.NewDiscoveries > 0)
        Console.WriteLine($"  New codex : {tracker.NewDiscoveries} first "
                          + (tracker.NewDiscoveries == 1 ? "discovery" : "discoveries") + " this session");

    if (session.Pending.Count > 0)
    {
        Console.WriteLine("  --- analysed, waiting on Vista Genomics ---");
        Console.WriteLine($"      {"value cr",12}  {"if first",12}  species");
        foreach (var p in session.Pending)
            Console.WriteLine($"      {p.Value,12:N0}  {p.Value * 5,12:N0}  {p.SpeciesName} ({p.BodyName})");
    }

    if (bodies.Count > 0)
    {
        Console.WriteLine("  --- bodies with bio signals ---");
        foreach (var b in bodies.Take(10))
        {
            var range = b.ValueRange is { } r2 ? $"{r2.Min,12:N0} – {r2.Max,12:N0} cr" : "  (FSS only — map it)";
            Console.WriteLine($"      {b.SignalCount,2}  {range}  {b.BodyName}");
        }
    }
}

/// <summary>
/// Cost an engineering roll against the live inventory — the shopping-list answer to
/// "what do I still need for G5 Dirty Drives?". With no blueprint id, lists what can be planned.
/// </summary>
static void PrintEngineeringPlan(string blueprintId, int grade, int rolls, CommanderState s)
{
    var catalog = EngineeringCatalog.Default;

    if (blueprintId.Length == 0 || catalog.Blueprint(blueprintId) is null)
    {
        if (blueprintId.Length > 0)
            Console.WriteLine($"\nUnknown blueprint '{blueprintId}'.");
        Console.WriteLine("\n======== Blueprints ========");
        foreach (var b in catalog.Blueprints.OrderBy(b => b.Module).ThenBy(b => b.Name))
            Console.WriteLine($"  {b.Id,-40}  {b.Module} — {b.Name} (to G{b.MaxGrade})");
        return;
    }

    var blueprint = catalog.Blueprint(blueprintId)!;
    var plan = EngineeringPlanner.Plan(blueprintId, grade, s, rolls);

    Console.WriteLine($"\n======== Engineering plan ========");
    Console.WriteLine($"  {blueprint.Module} — {blueprint.Name}  G{grade} x{rolls}");

    if (plan.Materials.Count == 0)
    {
        Console.WriteLine($"  Grade {grade} is not defined for this blueprint (max G{blueprint.MaxGrade}).");
        return;
    }

    Console.WriteLine($"  {(plan.Ready ? "Ready to roll — everything aboard." : $"{plan.Shopping.Count} material(s) short, {plan.TotalShortfall:N0} unit(s) to find.")}");
    Console.WriteLine($"      {"held",6} {"need",6} {"short",6}  material");
    foreach (var m in plan.Materials)
    {
        var mark = m.Satisfied ? "  " : "! ";
        Console.WriteLine($"    {mark}{m.Held,6:N0} {m.Needed,6:N0} {m.Shortfall,6:N0}  {m.Name} (G{m.Grade} {m.Category.ToLowerInvariant()})");
        foreach (var t in EngineeringPlanner.TradeOptions(m, s, limit: 2))
            Console.WriteLine($"            → trade {t.Cost:N0} x {t.Source.Name} (G{t.Source.Grade})");
    }
}

/// <summary>
/// The engineer unlock path: where the commander stands with each, and the one thing to do next.
/// Silent when the journal has never mentioned an engineer, since then there is nothing to report.
/// </summary>
static void PrintEngineers(EngineeringTracker tracker)
{
    var standings = tracker.Standings();
    if (standings.All(x => x.Status == EngineerStatus.Unknown)) return;

    var unlocked = standings.Count(x => x.IsUnlocked);
    var maxed = standings.Count(x => x.IsMaxed);

    Console.WriteLine("\n======== Engineers ========");
    Console.WriteLine($"  Unlocked  : {unlocked} of {standings.Count}  ({maxed} at grade 5)");

    var todo = standings.Where(x => !x.IsMaxed).ToList();
    if (todo.Count == 0)
    {
        Console.WriteLine("  Every engineer is at grade 5 — nothing left to unlock.");
        return;
    }

    Console.WriteLine("  --- next step, most actionable first ---");
    foreach (var s in todo.Take(12))
    {
        Console.WriteLine($"    {s.StatusLabel,-8} {s.Engineer.Name}  ({s.Engineer.Location})");
        Console.WriteLine($"             {s.NextStep}");
    }
    if (todo.Count > 12) Console.WriteLine($"    … and {todo.Count - 12} more.");
}

/// <summary>
/// Pilot rank standing across the five tracked ladders. Elite ranks are flagged with a star; a rank
/// at the top of its ladder shows "max" rather than a meaningless percentage.
/// </summary>
static void PrintRanks(RankTracker tracker)
{
    Console.WriteLine();
    Console.WriteLine("======== Ranks ========");
    if (!tracker.HasData)
    {
        Console.WriteLine("  No Rank/Progress events in this journal.");
        return;
    }

    foreach (var rank in tracker.All)
    {
        var progress = rank.IsMaxed ? "max" : $"{rank.Percent,3}%";
        Console.WriteLine($"  {rank.Label,-13}: {rank.Name,-18} {progress}{(rank.IsElite ? "  *" : "")}");
    }
}
