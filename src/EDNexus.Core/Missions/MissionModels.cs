namespace EDNexus.Core.Missions;

/// <summary>
/// One accepted mission, as the journal describes it.
/// </summary>
/// <param name="MissionId">The game's mission id — stable for the mission's whole life.</param>
/// <param name="Title">Localised mission name, e.g. "Kill 42 Pirates".</param>
/// <param name="GiverFaction">Faction that issued the mission — who your influence and reputation move with.</param>
/// <param name="TargetFaction">Faction to be killed/hit, for missions that name one.</param>
/// <param name="TargetType">Localised target description, e.g. "Pirates".</param>
/// <param name="KillCount">Kills the mission asks for; 0 for missions that are not kill missions.</param>
/// <param name="DestinationSystem">Where the mission is handed in, once the game has said.</param>
/// <param name="DestinationStation">Station to hand in at, once the game has said.</param>
/// <param name="Expiry">When the mission expires, when it has a deadline.</param>
/// <param name="Reward">Credit reward the mission was offered at.</param>
/// <param name="Influence">Influence swing as the game grades it ("+", "++", "+++").</param>
/// <param name="Reputation">Reputation swing, graded the same way.</param>
/// <param name="IsWing">Whether the mission is wing-shareable, which is what makes massacre stacks work.</param>
/// <param name="Accepted">When it was accepted.</param>
/// <param name="ReadyToTurnIn">
/// True once the game has redirected the mission to its hand-in — for kill missions that means the
/// kills are done.
/// </param>
public sealed record Mission(
    long MissionId,
    string Title,
    string GiverFaction,
    string? TargetFaction,
    string? TargetType,
    int KillCount,
    string? DestinationSystem,
    string? DestinationStation,
    DateTimeOffset? Expiry,
    long Reward,
    string? Influence,
    string? Reputation,
    bool IsWing,
    DateTimeOffset Accepted,
    bool ReadyToTurnIn = false)
{
    /// <summary>A kill mission is one that names a body count.</summary>
    public bool IsKillMission => KillCount > 0;

    /// <summary>How long is left, or null when the mission has no deadline.</summary>
    public TimeSpan? TimeLeft(DateTimeOffset now) => Expiry is { } e ? e - now : null;

    /// <summary>True when the deadline has passed.</summary>
    public bool IsExpired(DateTimeOffset now) => Expiry is { } e && e <= now;
}

/// <summary>
/// A set of missions that can be worked at the same time because they ask for the same kills — the
/// thing a massacre stack is built out of.
/// </summary>
/// <param name="TargetFaction">Faction being killed.</param>
/// <param name="TargetType">What kind of target, e.g. "Pirates".</param>
/// <param name="Missions">Every held mission against that target, richest first.</param>
public sealed record MissionStack(string TargetFaction, string? TargetType, IReadOnlyList<Mission> Missions)
{
    /// <summary>
    /// Kills the whole stack asks for. Note this is not how many you must fly: killing one ship
    /// counts towards every mission in the stack at once, so the number you actually need is the
    /// largest single mission, not this total.
    /// </summary>
    public int TotalKills => Missions.Sum(m => m.KillCount);

    /// <summary>The kills actually required to clear the stack — the biggest mission in it.</summary>
    public int KillsToClear => Missions.Count == 0 ? 0 : Missions.Max(m => m.KillCount);

    /// <summary>Total credits the stack pays out.</summary>
    public long TotalReward => Missions.Sum(m => m.Reward);

    /// <summary>The distinct factions that issued these missions — one line per source board.</summary>
    public IReadOnlyList<string> GiverFactions =>
        Missions.Select(m => m.GiverFaction)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>How many of the stack's missions are already flagged for hand-in.</summary>
    public int ReadyCount => Missions.Count(m => m.ReadyToTurnIn);
}

/// <summary>Missions waiting to be handed in at one place, so a single stop clears them all.</summary>
public sealed record TurnInGroup(string? System, string? Station, IReadOnlyList<Mission> Missions)
{
    public long TotalReward => Missions.Sum(m => m.Reward);

    /// <summary>True when every mission here is flagged ready — the stop is worth making now.</summary>
    public bool AllReady => Missions.Count > 0 && Missions.All(m => m.ReadyToTurnIn);
}
