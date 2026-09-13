using System.Collections.Generic;
using EDNexus.Core.Colonisation;
using EDNexus.Core.Exobio;
using EDNexus.Core.Overlay;
using EDNexus.Core.Settings;
using EDNexus.Core.State;
using Xunit;

namespace EDNexus.Tests.Overlay;

public class OverlayContentBuilderTests
{
    [Fact]
    public void Empty_state_produces_empty_content()
    {
        var state = new CommanderState();

        var content = OverlayContentBuilder.Build(state, currentBodySignals: null, activeSite: null, route: null);

        Assert.Null(content.StarSystem);
        Assert.Null(content.NextJumpSystem);
        Assert.Equal(0, content.FuelPercent);
        Assert.False(content.FuelLow);
        Assert.False(content.HasBioSignals);
        Assert.False(content.HasColonisationShortfall);
    }

    [Fact]
    public void Fuel_low_flips_once_capacity_and_main_are_known()
    {
        var state = new CommanderState { FuelCapacity = 32, FuelMain = 6 };

        var content = OverlayContentBuilder.Build(state, null, null, null);

        Assert.True(content.FuelLow);
        Assert.Equal(6.0 / 32.0, content.FuelPercent, precision: 4);
    }

    [Fact]
    public void Fuel_above_threshold_is_not_low()
    {
        var state = new CommanderState { FuelCapacity = 32, FuelMain = 20 };

        var content = OverlayContentBuilder.Build(state, null, null, null);

        Assert.False(content.FuelLow);
    }

    [Fact]
    public void Bio_signals_surface_only_when_current_body_has_any()
    {
        var state = new CommanderState();
        var noSignals = new BodyBioSignals(new BodyKey(1, 2), "Some Body", 0, System.Array.Empty<BioGenus>(), false);

        var content = OverlayContentBuilder.Build(state, noSignals, null, null);

        Assert.False(content.HasBioSignals);
        Assert.Null(content.BioSignalBody);

        var withSignals = new BodyBioSignals(new BodyKey(1, 2), "Some Body", 3, System.Array.Empty<BioGenus>(), true);
        var content2 = OverlayContentBuilder.Build(state, withSignals, null, null);

        Assert.True(content2.HasBioSignals);
        Assert.Equal("Some Body", content2.BioSignalBody);
        Assert.Equal(3, content2.BioSignalCount);
    }

    [Fact]
    public void Colonisation_shortfall_is_sorted_worst_first_and_capped()
    {
        var state = new CommanderState();
        var resources = new List<ColonisationResource>
        {
            new("Aluminium", "aluminium", Required: 100, Provided: 90, Payment: 100),   // 10 remaining
            new("Steel", "steel", Required: 200, Provided: 0, Payment: 100),            // 200 remaining
            new("Titanium", "titanium", Required: 50, Provided: 50, Payment: 100),      // complete — excluded
        };
        var site = new ColonisationSite { MarketId = 1, Resources = resources };

        var content = OverlayContentBuilder.Build(state, null, site, null);

        Assert.True(content.HasColonisationShortfall);
        Assert.Equal(2, content.ColonisationShortfalls.Count);
        Assert.Equal("Steel", content.ColonisationShortfalls[0].Name);      // biggest shortfall first
        Assert.Equal(200, content.ColonisationShortfalls[0].Remaining);
        Assert.Equal("Aluminium", content.ColonisationShortfalls[1].Name);
    }

    [Fact]
    public void Colonisation_shortfall_accounts_for_cargo_already_aboard()
    {
        var state = new CommanderState();
        state.Cargo["aluminium"] = 10;   // fully covers the 10-ton shortfall
        var resources = new List<ColonisationResource>
        {
            new("Aluminium", "aluminium", Required: 100, Provided: 90, Payment: 100),
        };
        var site = new ColonisationSite { MarketId = 1, Resources = resources };

        var content = OverlayContentBuilder.Build(state, null, site, null);

        Assert.False(content.HasColonisationShortfall);
    }

    [Fact]
    public void Next_jump_reads_the_saved_route_hop_at_step_index()
    {
        var state = new CommanderState();
        var route = new RouteSettings
        {
            StepIndex = 1,
            Hops = new List<SavedRouteHop>
            {
                new() { System = "Sol" },
                new() { System = "Deciat" },
                new() { System = "Maia" },
            },
        };

        var content = OverlayContentBuilder.Build(state, null, null, route);

        Assert.Equal("Deciat", content.NextJumpSystem);
    }

    [Fact]
    public void Next_jump_is_null_without_a_saved_route()
    {
        var state = new CommanderState();

        Assert.Null(OverlayContentBuilder.Build(state, null, null, null).NextJumpSystem);
        Assert.Null(OverlayContentBuilder.Build(state, null, null, new RouteSettings()).NextJumpSystem);
    }
}
