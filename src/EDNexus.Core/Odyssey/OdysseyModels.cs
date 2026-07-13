namespace EDNexus.Core.Odyssey;

/// <summary>An Odyssey (on-foot) engineer: where they are and how to gain access. Reference data, loaded from JSON.</summary>
/// <param name="Id">Stable slug used by mod references (e.g. "oden_geiger").</param>
/// <param name="Name">Exact in-game name, matched against the journal's <c>EngineerProgress</c> event
/// (Odyssey engineers appear in the same event as ship engineers).</param>
/// <param name="Unlock">Human-readable invitation / unlock requirement.</param>
public sealed record OdysseyEngineer(
    string Id,
    string Name,
    string System,
    string Base,
    string Unlock);

/// <summary>An on-foot good/component/data item and where it is farmed. Reference data, loaded from JSON.</summary>
/// <param name="Symbol">Journal symbol (lowercase), matching <c>CommanderState.OnFoot</c> keys.</param>
/// <param name="Category">Item / Component / Data / Consumable — selects which on-foot inventory the held count comes from.</param>
/// <param name="Source">Human-readable "where to find it", shown as the row tooltip.</param>
public sealed record OnFootMaterial(
    string Symbol,
    string Name,
    string Category,
    string Source);

/// <summary>One material line inside a grade step or modification recipe, before joining to held stock.</summary>
public sealed record MaterialCost(string Symbol, int Count);

/// <summary>
/// The cost to reach one grade from the grade below: exact material counts plus a credit cost, paid at
/// any Pioneer Supplies vendor. Unlike ship-engineering blueprints, Odyssey grade steps have confirmed
/// exact quantities.
/// </summary>
/// <param name="Grade">The grade this step reaches (2-5; there is no step "into" grade 1).</param>
/// <param name="CreditsEstimated">True when the wiki did not list a confirmed credit cost and the value
/// here was extrapolated from the cost progression of a comparable item — flagged so the UI can note it.</param>
public sealed record GradeStep(
    int Grade,
    long Credits,
    IReadOnlyList<MaterialCost> Materials,
    bool CreditsEstimated = false);

/// <summary>A suit and its grade-upgrade path. Reference data, loaded from JSON.</summary>
/// <param name="SuitSymbol">Journal <c>SuitName</c> prefix (before the "_classN" suffix), used to detect the equipped suit.</param>
/// <param name="ModSlotsByGrade">Mod slot count at each grade, indexed 0 (grade 1) through 4 (grade 5).</param>
/// <param name="GradeSteps">Empty for suits that cannot be upgraded (the Flight Suit).</param>
public sealed record Suit(
    string Id,
    string Name,
    string SuitSymbol,
    IReadOnlyList<int> ModSlotsByGrade,
    IReadOnlyList<GradeStep> GradeSteps)
{
    public GradeStep? Step(int grade) => GradeSteps.FirstOrDefault(g => g.Grade == grade);
    public bool IsUpgradeable => GradeSteps.Count > 0;
}

/// <summary>A handheld weapon and its grade-upgrade path. Reference data, loaded from JSON.</summary>
public sealed record Weapon(
    string Id,
    string Name,
    IReadOnlyList<int> ModSlotsByGrade,
    IReadOnlyList<GradeStep> GradeSteps)
{
    public GradeStep? Step(int grade) => GradeSteps.FirstOrDefault(g => g.Grade == grade);
}

/// <summary>A suit or weapon modification: its effect, material recipe, and offering engineers. Reference data, loaded from JSON.</summary>
public sealed record Modification(
    string Id,
    string Name,
    string AppliesTo,
    string Effect,
    IReadOnlyList<string> EngineerIds,
    long Credits,
    IReadOnlyList<MaterialCost> Materials);

/// <summary>One material a suit/weapon upgrade needs, joined with the commander's current on-foot stock.</summary>
/// <param name="Needed">Exact quantity required for this upgrade (unlike ship materials, this is a real count).</param>
/// <param name="Held">How many the commander currently has (0 if none / unknown material).</param>
public sealed record UpgradeRequirement(
    string Symbol,
    string Name,
    string Category,
    int Needed,
    int Held,
    string Source)
{
    public bool Satisfied => Held >= Needed;
    public int Shortfall => Math.Max(0, Needed - Held);
}

/// <summary>A modification applicable to the pinned suit/weapon, with its engineer resolved.</summary>
public sealed record ModOption(
    Modification Modification,
    OdysseyEngineer? Engineer,
    bool EngineerUnlocked);

/// <summary>
/// The computed picture for a pinned suit or weapon upgrade: the cumulative material checklist from the
/// current grade to the target, the total credit cost, and the modifications available once there.
/// </summary>
/// <param name="FromGrade">Current grade (1 if unknown / not yet equipped).</param>
/// <param name="ToGrade">Target grade being pinned to.</param>
public sealed record SuitUpgradePlan(
    Suit Suit,
    int FromGrade,
    int ToGrade,
    long TotalCredits,
    bool CreditsEstimated,
    IReadOnlyList<UpgradeRequirement> Materials,
    IReadOnlyList<ModOption> Mods);

/// <summary>The equivalent computed plan for a weapon upgrade (weapons have no live "current grade" detection).</summary>
public sealed record WeaponUpgradePlan(
    Weapon Weapon,
    int FromGrade,
    int ToGrade,
    long TotalCredits,
    bool CreditsEstimated,
    IReadOnlyList<UpgradeRequirement> Materials,
    IReadOnlyList<ModOption> Mods);
