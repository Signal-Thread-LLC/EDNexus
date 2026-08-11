using System.Linq;
using EDNexus.Core.Engineering;
using EDNexus.Core.State;
using Xunit;

namespace EDNexus.Tests.Engineering;

public class EngineeringPlannerTests
{
    // Increased FSD Range G5 costs one each of arsenic, chemical manipulators and datamined wake
    // exceptions — cross-checked between coriolis-data and the EDSM-verified list it replaced.
    private const string Fsd = "fsd_increased_range";

    private static CommanderState StateWith(params (string Category, string Symbol, int Count)[] held)
    {
        var s = new CommanderState();
        foreach (var (category, symbol, count) in held)
        {
            var bucket = category switch
            {
                "Raw" => s.Materials.Raw,
                "Manufactured" => s.Materials.Manufactured,
                _ => s.Materials.Encoded,
            };
            bucket[symbol] = count;
        }
        return s;
    }

    // --- Acceptance: "selecting a blueprint+grade lists exactly the missing materials and quantities". ---

    [Fact]
    public void An_empty_hold_is_short_every_material_by_its_full_requirement()
    {
        var plan = EngineeringPlanner.Plan(Fsd, 5, new CommanderState());

        Assert.Equal(3, plan.Materials.Count);
        Assert.All(plan.Materials, m =>
        {
            Assert.Equal(1, m.Needed);
            Assert.Equal(0, m.Held);
            Assert.Equal(1, m.Shortfall);
            Assert.False(m.Satisfied);
        });
        Assert.False(plan.Ready);
        Assert.Equal(3, plan.TotalShortfall);
    }

    [Fact]
    public void The_shopping_list_holds_only_what_is_missing()
    {
        var state = StateWith(("Raw", "arsenic", 5), ("Encoded", "dataminedwake", 5));
        var plan = EngineeringPlanner.Plan(Fsd, 5, state);

        var missing = Assert.Single(plan.Shopping);
        Assert.Equal("chemicalmanipulators", missing.Symbol);
        Assert.Equal(1, missing.Shortfall);
        Assert.Equal(1, plan.TotalShortfall);
    }

    [Fact]
    public void A_covered_hold_reports_ready_with_an_empty_shopping_list()
    {
        var state = StateWith(
            ("Raw", "arsenic", 5),
            ("Encoded", "dataminedwake", 5),
            ("Manufactured", "chemicalmanipulators", 5));

        var plan = EngineeringPlanner.Plan(Fsd, 5, state);

        Assert.True(plan.Ready);
        Assert.Empty(plan.Shopping);
        Assert.Equal(0, plan.TotalShortfall);
        Assert.All(plan.Materials, m => Assert.True(m.Satisfied));
    }

    // --- Acceptance: "shortfall updates live as materials are collected or spent". ---

    [Fact]
    public void Shortfall_follows_the_inventory_as_it_changes()
    {
        var state = new CommanderState();
        Assert.Equal(1, EngineeringPlanner.Plan(Fsd, 5, state).Materials.Single(m => m.Symbol == "arsenic").Shortfall);

        state.Materials.Raw["arsenic"] = 1;          // collected one
        Assert.Equal(0, EngineeringPlanner.Plan(Fsd, 5, state).Materials.Single(m => m.Symbol == "arsenic").Shortfall);

        state.Materials.Raw["arsenic"] = 0;          // spent it again
        Assert.Equal(1, EngineeringPlanner.Plan(Fsd, 5, state).Materials.Single(m => m.Symbol == "arsenic").Shortfall);
    }

    // --- Queue of rolls. ---

    [Fact]
    public void Multiple_rolls_of_one_grade_multiply_the_requirement()
    {
        var plan = EngineeringPlanner.Plan(Fsd, 5, new CommanderState(), rolls: 5);

        Assert.All(plan.Materials, m => Assert.Equal(5, m.Needed));
        Assert.Equal(15, plan.TotalShortfall);
    }

    [Fact]
    public void A_queue_sums_materials_shared_between_its_rolls()
    {
        // G4 and G5 of the same blueprint have distinct ingredient lists; queue them together.
        var g4 = EngineeringPlanner.Plan(Fsd, 4, new CommanderState()).Materials.ToDictionary(m => m.Symbol, m => m.Needed);
        var g5 = EngineeringPlanner.Plan(Fsd, 5, new CommanderState()).Materials.ToDictionary(m => m.Symbol, m => m.Needed);

        var plan = EngineeringPlanner.Plan(
            new[] { new PlannedRoll(Fsd, 4), new PlannedRoll(Fsd, 5) }, new CommanderState());

        var expected = g4.Keys.Union(g5.Keys).ToDictionary(
            k => k, k => g4.GetValueOrDefault(k) + g5.GetValueOrDefault(k));

        Assert.Equal(expected.Count, plan.Materials.Count);
        foreach (var m in plan.Materials)
            Assert.Equal(expected[m.Symbol], m.Needed);
    }

    [Fact]
    public void Rolls_of_the_same_grade_across_queue_entries_accumulate()
    {
        var plan = EngineeringPlanner.Plan(
            new[] { new PlannedRoll(Fsd, 5, 2), new PlannedRoll(Fsd, 5, 3) }, new CommanderState());

        Assert.All(plan.Materials, m => Assert.Equal(5, m.Needed));
    }

    [Fact]
    public void An_unknown_blueprint_or_grade_contributes_nothing_rather_than_throwing()
    {
        var plan = EngineeringPlanner.Plan(
            new[] { new PlannedRoll("no_such_blueprint", 5), new PlannedRoll(Fsd, 99), new PlannedRoll(Fsd, 5) },
            new CommanderState());

        Assert.Equal(3, plan.Materials.Count);   // only the one real roll counted
    }

    [Fact]
    public void A_zero_or_negative_roll_count_is_dropped()
    {
        var plan = EngineeringPlanner.Plan(new[] { new PlannedRoll(Fsd, 5, 0) }, new CommanderState());
        Assert.Empty(plan.Materials);
        Assert.True(plan.Ready);   // nothing needed, so nothing is missing
    }

    // --- Trader hints (the "trade up" half of the issue). ---

    [Fact]
    public void A_shortfall_suggests_the_cheapest_trade_from_what_is_held()
    {
        // Arsenic is raw grade 2; hold plenty of mercury (same group, grade 3) to trade down.
        var state = StateWith(("Raw", "mercury", 50));
        var plan = EngineeringPlanner.Plan(Fsd, 5, state);
        var arsenic = plan.Materials.Single(m => m.Symbol == "arsenic");

        var options = EngineeringPlanner.TradeOptions(arsenic, state);

        Assert.NotEmpty(options);
        Assert.Equal("mercury", options[0].Source.Symbol);
        Assert.True(options[0].Affordable);
    }

    [Fact]
    public void A_satisfied_material_suggests_no_trades()
    {
        var state = StateWith(("Raw", "arsenic", 10), ("Raw", "mercury", 50));
        var arsenic = EngineeringPlanner.Plan(Fsd, 5, state).Materials.Single(m => m.Symbol == "arsenic");

        Assert.Empty(EngineeringPlanner.TradeOptions(arsenic, state));
    }

    // --- Catalog breadth: the dataset this issue was blocked on. ---

    [Fact]
    public void Every_blueprint_grade_carries_material_quantities()
    {
        var catalog = EngineeringCatalog.Default;
        Assert.Equal(80, catalog.Blueprints.Count);

        foreach (var bp in catalog.Blueprints)
        {
            Assert.NotEmpty(bp.Grades);
            foreach (var grade in bp.Grades)
            {
                Assert.NotEmpty(grade.Materials);
                Assert.All(grade.Materials, m => Assert.True(m.Count > 0));
            }
        }
    }

    [Fact]
    public void The_six_original_blueprint_ids_survive_so_an_existing_pin_still_resolves()
    {
        var catalog = EngineeringCatalog.Default;
        foreach (var id in new[]
                 {
                     "fsd_increased_range", "thrusters_dirty", "shieldgen_reinforced",
                     "armour_heavyduty", "powerplant_overcharged", "powerdist_engfocused",
                 })
            Assert.True(catalog.Blueprint(id) is not null, $"pinned id '{id}' no longer resolves");
    }
}
