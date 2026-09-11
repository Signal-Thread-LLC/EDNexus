namespace EDNexus.Core.CommunityGoals;

/// <summary>
/// One active (or recently completed) Community Goal, as the journal describes it.
/// </summary>
/// <param name="CGID">The game's community goal id — stable for the goal's whole life.</param>
/// <param name="Title">The goal's headline, e.g. "Battle for the Core".</param>
/// <param name="System">System the goal is running in, when the game has said.</param>
/// <param name="Station">Station/market the goal is tied to, when the game has said.</param>
/// <param name="Expiry">When the goal closes, when the game has said.</param>
/// <param name="IsComplete">True once the goal has finished — set by the goal itself or a reward payout.</param>
/// <param name="CurrentTotal">Total contribution logged by every commander, as of the last snapshot.</param>
/// <param name="PlayerContribution">This commander's own contribution, as of the last snapshot.</param>
/// <param name="NumContributors">How many commanders have contributed, when the game has said.</param>
/// <param name="TopRankSize">Size of the top-rank band (e.g. top 10), when the game has said.</param>
/// <param name="TopTierName">Name of the highest tier on offer, e.g. "Tier 5".</param>
/// <param name="TopTierBonus">
/// Reward text for the top tier, as the game writes it — kept as a string since it is not always a
/// plain credit figure.
/// </param>
/// <param name="TierReached">The tier this commander (or the goal) has reached so far.</param>
/// <param name="PlayerInTopRank">Whether this commander currently sits inside the top-rank band.</param>
/// <param name="PlayerPercentileBand">This commander's percentile band among contributors, when known.</param>
/// <param name="Joined">Whether this commander has explicitly joined the goal via its board.</param>
/// <param name="Rewarded">True once a completion payout has been logged for this goal.</param>
/// <param name="RewardAmount">Credits paid out, once rewarded.</param>
/// <param name="RewardBonus">Bonus credits paid out alongside the reward, when the game reports one.</param>
public sealed record CommunityGoal(
    long CGID,
    string Title,
    string? System,
    string? Station,
    DateTimeOffset? Expiry,
    bool IsComplete,
    long CurrentTotal,
    long PlayerContribution,
    long? NumContributors,
    int? TopRankSize,
    string? TopTierName,
    string? TopTierBonus,
    string? TierReached,
    bool? PlayerInTopRank,
    int? PlayerPercentileBand,
    bool Joined = false,
    bool Rewarded = false,
    long? RewardAmount = null,
    long? RewardBonus = null)
{
    /// <summary>How long is left, or null when the goal carries no expiry.</summary>
    public TimeSpan? TimeLeft(DateTimeOffset now) => Expiry is { } e ? e - now : null;

    /// <summary>True when the expiry has passed.</summary>
    public bool IsExpired(DateTimeOffset now) => Expiry is { } e && e <= now;
}
