using EDNexus.Core.Exobio;
using EDNexus.Core.Mining;
using EDNexus.Core.Missions;
using EDNexus.Core.Ranks;

namespace EDNexus.Core.Twitch;

/// <summary>
/// The feature trackers <see cref="StreamCardMapper"/> reads beyond <c>CommanderState</c> itself.
/// Every one is optional so the mapper can be exercised — and the card can run — with only the
/// pieces a given host has wired up.
/// </summary>
/// <param name="Ranks">Pilot rank standing across the five ladders.</param>
/// <param name="Exobiology">Sampling progress and the session's Vista Genomics tally.</param>
/// <param name="Mining">Prospected-asteroid history for the current session.</param>
/// <param name="Missions">Missions held and the stacks they form.</param>
public sealed record StreamCardSources(
    RankTracker? Ranks = null,
    ExobiologyTracker? Exobiology = null,
    MiningTracker? Mining = null,
    MissionTracker? Missions = null)
{
    /// <summary>No trackers at all — the card falls back to what <c>CommanderState</c> alone knows.</summary>
    public static readonly StreamCardSources Empty = new();
}
