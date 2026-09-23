using EDNexus.Core.Exobio;
using EDNexus.Core.Mining;
using EDNexus.Core.Missions;
using EDNexus.Core.Ranks;

namespace EDNexus.Core.Twitch;

/// <summary>
/// The feature trackers <see cref="StreamCardMapper"/> reads beyond <c>CommanderState</c>. All
/// optional, so a host can wire up only the pieces it has.
/// </summary>
public sealed record StreamCardSources(
    RankTracker? Ranks = null,
    ExobiologyTracker? Exobiology = null,
    MiningTracker? Mining = null,
    MissionTracker? Missions = null)
{
    public static readonly StreamCardSources Empty = new();
}
