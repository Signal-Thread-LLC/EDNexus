namespace EDNexus.Core.Ranks;

/// <summary>
/// The five pilot ranks EDNexus tracks. Each maps to one field on the journal's
/// <c>Rank</c>/<c>Progress</c>/<c>Promotion</c> events — note the field name is not always the
/// name the game shows the commander (see <see cref="RankLadder.JournalField"/>).
/// </summary>
public enum RankKind
{
    Combat,
    Trade,
    Explore,
    Exobiologist,
    Mercenary,
}

/// <summary>
/// One rank's ladder: how the game's numeric rank index maps to the name a commander recognises.
/// </summary>
/// <param name="Kind">Which rank this describes.</param>
/// <param name="JournalField">
/// The property name carrying this rank on the journal events. This is the game's own spelling and
/// differs from <paramref name="Label"/> for Mercenary, which the journal calls <c>Soldier</c>.
/// </param>
/// <param name="Label">The name shown in the UI.</param>
/// <param name="Names">Rank names by index, from 0 up through the Elite tiers.</param>
public sealed record RankLadder(RankKind Kind, string JournalField, string Label, IReadOnlyList<string> Names)
{
    /// <summary>Index at which a ladder reaches Elite; everything above is an Elite tier.</summary>
    public const int EliteIndex = 8;

    /// <summary>
    /// Name for a rank index. Unknown indices are reported rather than clamped — if Frontier adds a
    /// tier, showing "Rank 14" is honest, where silently pinning to Elite V would not be.
    /// </summary>
    public string NameFor(int index) =>
        index >= 0 && index < Names.Count ? Names[index] : $"Rank {index}";

    /// <summary>True once the commander is Elite or beyond in this rank.</summary>
    public static bool IsElite(int index) => index >= EliteIndex;

    /// <summary>The top of the ladder — no further promotion exists.</summary>
    public int MaxIndex => Names.Count - 1;
}

/// <summary>A rank's current standing: where the commander sits and how far to the next tier.</summary>
/// <param name="Kind">Which rank.</param>
/// <param name="Label">Display name of the rank track.</param>
/// <param name="Index">The game's numeric rank index.</param>
/// <param name="Name">Ladder name for <paramref name="Index"/>.</param>
/// <param name="Percent">Progress towards the next tier, 0–100.</param>
public sealed record RankProgress(RankKind Kind, string Label, int Index, string Name, int Percent)
{
    /// <summary>True once this rank is Elite or an Elite tier.</summary>
    public bool IsElite => RankLadder.IsElite(Index);

    /// <summary>True at the very top of the ladder, where the progress bar no longer means anything.</summary>
    public bool IsMaxed { get; init; }

    /// <summary>Progress as a 0–1 fraction, for binding to a bar.</summary>
    public double Fraction => Percent / 100d;
}

/// <summary>The ladders themselves. Names are the in-game rank titles, indexed as the journal numbers them.</summary>
public static class RankLadders
{
    // Every ladder shares the same Elite tail: index 8 is Elite, then the five Elite tiers added in
    // Odyssey. Kept in one place so the tiers stay consistent across all five tracks.
    private static readonly string[] EliteTail =
    {
        "Elite", "Elite I", "Elite II", "Elite III", "Elite IV", "Elite V",
    };

    private static IReadOnlyList<string> Ladder(params string[] beforeElite) =>
        beforeElite.Concat(EliteTail).ToArray();

    public static readonly RankLadder Combat = new(
        RankKind.Combat, "Combat", "Combat",
        Ladder("Harmless", "Mostly Harmless", "Novice", "Competent", "Expert", "Master", "Dangerous", "Deadly"));

    public static readonly RankLadder Trade = new(
        RankKind.Trade, "Trade", "Trade",
        Ladder("Penniless", "Mostly Penniless", "Peddler", "Dealer", "Merchant", "Broker", "Entrepreneur", "Tycoon"));

    public static readonly RankLadder Explore = new(
        RankKind.Explore, "Explore", "Explorer",
        Ladder("Aimless", "Mostly Aimless", "Scout", "Surveyor", "Trailblazer", "Pathfinder", "Ranger", "Pioneer"));

    public static readonly RankLadder Exobiologist = new(
        RankKind.Exobiologist, "Exobiologist", "Exobiologist",
        Ladder("Directionless", "Mostly Directionless", "Compiler", "Collector", "Cataloguer", "Taxonomist",
            "Ecologist", "Geneticist"));

    // The journal calls this rank "Soldier"; the game's UI calls it Mercenary, and so do we.
    public static readonly RankLadder Mercenary = new(
        RankKind.Mercenary, "Soldier", "Mercenary",
        Ladder("Defenceless", "Mostly Defenceless", "Rookie", "Soldier", "Gunslinger", "Warrior", "Gladiator",
            "Deadeye"));

    /// <summary>Every tracked ladder, in the order the card lists them.</summary>
    public static readonly IReadOnlyList<RankLadder> All = new[]
    {
        Combat, Trade, Explore, Exobiologist, Mercenary,
    };

    public static RankLadder For(RankKind kind) => All.First(l => l.Kind == kind);
}
