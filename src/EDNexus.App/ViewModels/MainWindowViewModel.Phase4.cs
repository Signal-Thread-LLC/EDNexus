using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDNexus.Core.Dev;
using EDNexus.Core.Routes;
using EDNexus.Core.State;
using EDNexus.Core.Trade;

namespace EDNexus.App.ViewModels;

// Phase 4 — the two network-backed cards (route plotting + trade search). Kept in a partial file so
// the request/response feature logic sits apart from the state-mirroring in the main view model.
public sealed partial class MainWindowViewModel
{
    private SampleRoutePlotter? _sampleRoutes;
    private SampleTradeSearch? _sampleTrade;

    /// <summary>In developer mode the cards run against offline generators; otherwise the live host services.</summary>
    private IRoutePlotter Plotter => _boot.Dev.Enabled ? _sampleRoutes ??= new SampleRoutePlotter(_rng) : _host.Routes;
    private ITradeSearch Trader => _boot.Dev.Enabled ? _sampleTrade ??= new SampleTradeSearch(_rng) : _host.Trade;

    // --- Route plotting ---------------------------------------------------------------------------

    [ObservableProperty] private string _routeFrom = "";
    [ObservableProperty] private string _routeTo = "";
    [ObservableProperty] private string _routeJumpRange = "50";
    [ObservableProperty] private string _routeStatus = "";
    [ObservableProperty] private string _routeSummary = "";
    [ObservableProperty] private bool _routeBusy;
    [ObservableProperty] private bool _routeHasPlan;
    [ObservableProperty] private string _routeNextSystem = "—";
    [ObservableProperty] private int _routeStepIndex;

    public ObservableCollection<RouteHopLine> RouteHops { get; } = new();

    [RelayCommand]
    private async Task PlotRoute()
    {
        var from = RouteFrom.Trim();
        var to = RouteTo.Trim();
        if (from.Length == 0 || to.Length == 0)
        {
            RouteStatus = "Enter a start and destination system.";
            return;
        }
        if (!TryParseRange(RouteJumpRange, out var range))
        {
            RouteStatus = "Enter the ship's jump range in light years (e.g. 48.5).";
            return;
        }

        RouteBusy = true;
        RouteStatus = $"Plotting {from} → {to} …";
        RouteHasPlan = false;
        RouteHops.Clear();
        try
        {
            var plan = await Plotter.PlotAsync(new RoutePlotRequest(from, to, range), CancellationToken.None);
            if (plan is null || plan.Hops.Count == 0)
            {
                RouteStatus = "No route found. Check the system names and that the ship's range can bridge the gap.";
                return;
            }

            for (var i = 0; i < plan.Hops.Count; i++)
            {
                var h = plan.Hops[i];
                RouteHops.Add(new RouteHopLine(
                    number: i,
                    system: h.System,
                    jumps: i == 0 ? "start" : h.Jumps == 1 ? "1 jump" : $"{h.Jumps} jumps",
                    isNeutron: h.IsNeutron,
                    remaining: $"{h.DistanceRemainingLy:N0} ly left"));
            }

            RouteHasPlan = true;
            RouteSummary = $"{plan.WaypointCount} waypoints · {plan.TotalJumps} jumps · {plan.NeutronCount} neutron boosts";
            RouteStatus = $"via {Plotter.SourceName}";
            SetRouteStep(Math.Min(1, RouteHops.Count - 1));   // first target after the origin

            // Straight-line context is a nice-to-have; a failed EDSM lookup must not spoil the plot.
            _ = AnnotateDirectDistanceAsync(from, to);
        }
        catch (Exception ex)
        {
            RouteStatus = "Route plot failed: " + ex.Message;
        }
        finally
        {
            RouteBusy = false;
        }
    }

    /// <summary>Fold in the EDSM straight-line distance once it comes back, without blocking the plot.</summary>
    private async Task AnnotateDirectDistanceAsync(string from, string to)
    {
        if (_boot.Dev.Enabled) return;   // offline mode has no EDSM catalogue to consult
        try
        {
            var direct = await _host.Navigation.DistanceBetweenAsync(from, to, CancellationToken.None);
            if (direct is { } ly && RouteHasPlan)
                RouteSummary += $" · {ly:N0} ly direct";
        }
        catch { /* best effort — the route itself is already shown */ }
    }

    [RelayCommand]
    private async Task CopyNextSystem()
    {
        if (RouteHops.Count == 0) return;
        var system = RouteHops[Math.Clamp(RouteStepIndex, 0, RouteHops.Count - 1)].System;
        await CopyToClipboardAsync(system);
        RouteStatus = $"Copied “{system}” — paste into the galaxy map.";
    }

    [RelayCommand]
    private void NextHop()
    {
        if (RouteStepIndex < RouteHops.Count - 1) SetRouteStep(RouteStepIndex + 1);
    }

    [RelayCommand]
    private void PrevHop()
    {
        if (RouteStepIndex > 0) SetRouteStep(RouteStepIndex - 1);
    }

    /// <summary>Move the "next system" pointer, flipping the highlighted row so the list tracks progress.</summary>
    private void SetRouteStep(int index)
    {
        index = Math.Clamp(index, 0, Math.Max(0, RouteHops.Count - 1));
        for (var i = 0; i < RouteHops.Count; i++)
            RouteHops[i].IsCurrent = i == index;
        RouteStepIndex = index;
        RouteNextSystem = RouteHops.Count > 0 ? RouteHops[index].System : "—";
    }

    private static bool TryParseRange(string text, out double range) =>
        double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out range) && range > 0;

    // --- Trade search -----------------------------------------------------------------------------

    [ObservableProperty] private string _tradeCommodity = "";
    [ObservableProperty] private string _tradeReferenceSystem = "";
    [ObservableProperty] private bool _tradeSelling = true;   // true = offload the hold; false = source cargo
    [ObservableProperty] private string _tradeStatus = "";
    [ObservableProperty] private bool _tradeBusy;
    [ObservableProperty] private bool _tradeHasResults;

    public ObservableCollection<TradeResultLine> TradeResults { get; } = new();

    [RelayCommand]
    private async Task SearchTrade()
    {
        var commodity = TradeCommodity.Trim();
        var reference = TradeReferenceSystem.Trim();
        if (commodity.Length == 0)
        {
            TradeStatus = "Enter a commodity to look up.";
            return;
        }
        if (reference.Length == 0)
        {
            TradeStatus = "Enter the system to search from (usually your current one).";
            return;
        }

        var mode = TradeSelling ? TradeMode.Sell : TradeMode.Buy;
        TradeBusy = true;
        TradeHasResults = false;
        TradeResults.Clear();
        TradeStatus = TradeSelling ? $"Best places to sell {commodity} near {reference} …"
                                   : $"Best places to buy {commodity} near {reference} …";
        try
        {
            var quotes = await Trader.SearchAsync(new TradeQuery(commodity, reference, mode), CancellationToken.None);
            if (quotes.Count == 0)
            {
                TradeStatus = $"No station found {(TradeSelling ? "buying" : "selling")} {commodity} nearby.";
                return;
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var q in quotes)
                TradeResults.Add(new TradeResultLine(
                    q.System, q.Station,
                    $"{q.DistanceLy:0.0} ly",
                    $"{q.Price:N0} cr",
                    q.Quantity > 0 ? $"{q.Quantity:N0} {(TradeSelling ? "dmd" : "sup")}" : "",
                    HumanizeAge(q.Age(now))));

            TradeHasResults = true;
            TradeStatus = $"{quotes.Count} stations · via {Trader.SourceName}";
        }
        catch (Exception ex)
        {
            TradeStatus = "Trade search failed: " + ex.Message;
        }
        finally
        {
            TradeBusy = false;
        }
    }

    [RelayCommand]
    private async Task CopySystem(string? system)
    {
        if (string.IsNullOrWhiteSpace(system)) return;
        await CopyToClipboardAsync(system);
        TradeStatus = $"Copied “{system}”.";
    }

    private static string HumanizeAge(TimeSpan? age) => age switch
    {
        null => "",
        { TotalMinutes: < 60 } a => $"{a.TotalMinutes:0}m ago",
        { TotalHours: < 24 } a => $"{a.TotalHours:0}h ago",
        var a => $"{a.Value.TotalDays:0}d ago",
    };

    // --- Shared -----------------------------------------------------------------------------------

    /// <summary>
    /// Pre-fill the reference systems and commodity from live/dev state, but only while a field is
    /// still empty — never clobber what the commander has typed. Called from the state refresh loop.
    /// </summary>
    private void SeedPhase4Defaults(CommanderState s)
    {
        if (RouteFrom.Length == 0 && s.StarSystem is { Length: > 0 } sys) RouteFrom = sys;
        if (TradeReferenceSystem.Length == 0 && s.StarSystem is { Length: > 0 } sys2) TradeReferenceSystem = sys2;
        if (TradeCommodity.Length == 0 && !s.Cargo.IsEmpty)
            TradeCommodity = s.Cargo.OrderByDescending(k => k.Value).First().Key;
    }

    private static async Task CopyToClipboardAsync(string text)
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}

/// <summary>
/// One row on the route card: a waypoint with its jump cost, whether it is a neutron boost star, and
/// how far along the route it leaves the commander. <see cref="IsCurrent"/> marks the "next" hop the
/// stepper is pointing at so the list can highlight travel progress.
/// </summary>
public sealed partial class RouteHopLine : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public int Number { get; }
    public string System { get; }
    public string Jumps { get; }
    public bool IsNeutron { get; }
    public string Remaining { get; }

    [ObservableProperty] private bool _isCurrent;

    public RouteHopLine(int number, string system, string jumps, bool isNeutron, string remaining)
    {
        Number = number;
        System = system;
        Jumps = jumps;
        IsNeutron = isNeutron;
        Remaining = remaining;
    }
}

/// <summary>
/// One row on the trade card: a station, how far its system is from the reference, the quoted price,
/// the available quantity (demand when selling, supply when buying), and how stale the price is.
/// </summary>
public sealed record TradeResultLine(
    string System, string Station, string Distance, string Price, string Quantity, string Age);
