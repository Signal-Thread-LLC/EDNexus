using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDNexus.Core.Dev;
using EDNexus.Core.Routes;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>
/// Route plotter: a neutron-highway plot between two systems, with a stepper over the waypoints.
/// Backed by the live Spansh plotter, or an offline sample generator while developer mode is on.
/// </summary>
public sealed partial class RouteCardViewModel : CardViewModel
{
    private SampleRoutePlotter? _sampleRoutes;

    public RouteCardViewModel(DashboardContext context) : base(context, "route", "ROUTE PLOTTER", 452) { }

    /// <summary>No dev-mode sample source feeds this card, so it has no 🎲 reshuffle.</summary>
    public override bool CanRandomize => false;

    private IRoutePlotter Plotter => Context.DevEnabled ? _sampleRoutes ??= new SampleRoutePlotter(Context.Rng) : Context.Host.Routes;

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

    /// <summary>Pre-fill the origin from live state, but never clobber what the commander has typed.</summary>
    public override void Update(CommanderState s)
    {
        if (RouteFrom.Length == 0 && s.StarSystem is { Length: > 0 } sys) RouteFrom = sys;
    }

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
        if (Context.DevEnabled) return;   // offline mode has no EDSM catalogue to consult
        try
        {
            var direct = await Context.Host.Navigation.DistanceBetweenAsync(from, to, CancellationToken.None);
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
