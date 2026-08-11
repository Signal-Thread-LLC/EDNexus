namespace EDNexus.Core.Engineering;

/// <summary>An engineer: where they are and how to gain access. Reference data, loaded from JSON.</summary>
/// <param name="Id">Stable slug used by blueprint references (e.g. "farseer").</param>
/// <param name="Name">Exact in-game name, matched against the journal's <c>EngineerProgress</c> event.</param>
/// <param name="Unlock">Human-readable invitation / unlock requirement.</param>
public sealed record Engineer(
    string Id,
    string Name,
    string System,
    string Base,
    string Unlock,
    IReadOnlyList<string> Specialities);

/// <summary>A crafting material: where it is farmed, and what a material trader will swap it for.</summary>
/// <param name="Symbol">Journal symbol (lowercase), matching <c>CommanderState.Materials</c> keys.</param>
/// <param name="Category">Raw / Manufactured / Encoded — selects which inventory the held count comes from.</param>
/// <param name="Grade">Rarity 1–5 (raw materials stop at 4). Also fixes the storage cap.</param>
/// <param name="Source">Human-readable "where to find it", shown as the row tooltip. May be empty.</param>
/// <param name="Group">
/// The trader group this material sits in — one of the seven numbered columns for raw materials,
/// or a named family ("Alloys", "Wake Scans", …) for manufactured and encoded. Swapping inside a
/// group is cheaper than crossing to another, so <see cref="Materials.MaterialTrader"/> needs it.
/// </param>
public sealed record MaterialInfo(
    string Symbol,
    string Name,
    string Category,
    int Grade,
    string Source,
    string Group = "")
{
    /// <summary>
    /// How many of this material the commander can hold. The cap is a pure function of grade —
    /// the rarer the material, the fewer fit — and is the same for all three categories.
    /// </summary>
    public int Cap => Grade switch
    {
        1 => 300,
        2 => 250,
        3 => 200,
        4 => 150,
        _ => 100,
    };
}

/// <summary>A blueprint and its per-grade ingredient lists. Reference data, loaded from JSON.</summary>
public sealed record Blueprint(
    string Id,
    string Name,
    string Module,
    IReadOnlyList<BlueprintGrade> Grades)
{
    public BlueprintGrade? Grade(int grade) => Grades.FirstOrDefault(g => g.GradeValue == grade);

    /// <summary>Highest grade defined for this blueprint (usually 5).</summary>
    public int MaxGrade => Grades.Count == 0 ? 0 : Grades.Max(g => g.GradeValue);
}

/// <summary>One grade of a blueprint: which engineers can apply it and what it consumes.</summary>
/// <param name="GradeValue">The grade number, 1–5.</param>
/// <param name="EngineerIds">Ids of engineers offering this blueprint at (at least) this grade.</param>
/// <param name="Materials">What one roll at this grade consumes, with quantities.</param>
public sealed record BlueprintGrade(
    int GradeValue,
    IReadOnlyList<string> EngineerIds,
    IReadOnlyList<BlueprintMaterial> Materials);

/// <summary>One ingredient of a blueprint grade: a material symbol and how many a single roll costs.</summary>
public sealed record BlueprintMaterial(string Symbol, int Count);

/// <summary>
/// The computed picture for a pinned blueprint+grade: the engineer to visit and the material
/// checklist, each annotated with how much the commander already holds and where to farm it.
/// </summary>
/// <param name="Engineer">The chosen engineer (prefers one already unlocked), or null if none is known.</param>
/// <param name="EngineerUnlocked">True when the journal shows this engineer is unlocked.</param>
/// <param name="OtherEngineers">Alternative engineers offering the same grade, for context.</param>
public sealed record PinnedBlueprintPlan(
    Blueprint Blueprint,
    int Grade,
    Engineer? Engineer,
    bool EngineerUnlocked,
    IReadOnlyList<Engineer> OtherEngineers,
    IReadOnlyList<MaterialRequirement> Materials)
{
    /// <summary>Materials still short, scarcest first — the shopping list proper.</summary>
    public IReadOnlyList<MaterialRequirement> Shopping =>
        Materials.Where(m => !m.Satisfied).OrderByDescending(m => m.Grade).ThenBy(m => m.Name).ToList();

    /// <summary>Every material for this roll is already aboard.</summary>
    public bool Ready => Materials.All(m => m.Satisfied);
}

/// <summary>One material a plan needs, joined with the commander's current holdings.</summary>
/// <param name="Needed">How many the plan consumes in total (summed across a queue of rolls).</param>
/// <param name="Held">How many the commander currently has (0 if none / unknown material).</param>
public sealed record MaterialRequirement(
    string Symbol,
    string Name,
    string Category,
    int Grade,
    int Needed,
    int Held,
    string Source)
{
    /// <summary>How many still have to be found. 0 once the hold covers the requirement.</summary>
    public int Shortfall => Math.Max(0, Needed - Held);

    /// <summary>The hold already covers this material — nothing to farm.</summary>
    public bool Satisfied => Held >= Needed;

    /// <summary>Progress towards the requirement, 0–1, for a per-row bar.</summary>
    public double Fraction => Needed <= 0 ? 1 : Math.Clamp((double)Held / Needed, 0, 1);
}
