using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EDNexus.Core.CommunityGoals;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>
/// Community Goal tracker: every active goal seen nearby, with the tier reached, this commander's
/// contribution against the total, the current top-tier bonus, and time remaining.
/// </summary>
public sealed partial class CommunityGoalsCardViewModel : CardViewModel
{
    private string _signature = "";

    public CommunityGoalsCardViewModel(DashboardContext context) : base(context, "community-goals", "COMMUNITY GOALS", 452) { }

    [ObservableProperty] private bool _hasGoals;

    /// <summary>Inverse of <see cref="HasGoals"/>, for the empty-state hint's visibility.</summary>
    public bool NoGoals => !HasGoals;

    partial void OnHasGoalsChanged(bool value) => OnPropertyChanged(nameof(NoGoals));

    public ObservableCollection<CommunityGoalLine> Goals { get; } = new();

    public override void Update(CommanderState s)
    {
        var tracker = Context.Host.CommunityGoals;
        var active = tracker.Active;

        if (active.Count == 0)
        {
            if (HasGoals) { HasGoals = false; Goals.Clear(); _signature = ""; }
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // Rebuild only when something actually moved: the dashboard ticks four times a second.
        var signature = string.Join("|", active.Select(g =>
            $"{g.CGID}:{g.PlayerContribution}:{g.CurrentTotal}:{g.TierReached}:{g.Joined}:{g.Rewarded}"));
        if (signature == _signature) return;
        _signature = signature;

        HasGoals = true;

        Goals.Clear();
        foreach (var goal in active)
        {
            var where = string.IsNullOrWhiteSpace(goal.Station)
                ? goal.System ?? "Unknown system"
                : $"{goal.Station} · {goal.System}";

            var tier = string.IsNullOrWhiteSpace(goal.TierReached)
                ? "No tier reached yet"
                : $"Reached {goal.TierReached}";
            if (!string.IsNullOrWhiteSpace(goal.TopTierName))
                tier += string.IsNullOrWhiteSpace(goal.TopTierBonus)
                    ? $" · top {goal.TopTierName}"
                    : $" · top {goal.TopTierName} ({goal.TopTierBonus} cr)";

            var contribution = $"{goal.PlayerContribution:N0} cr of {goal.CurrentTotal:N0} cr total";

            var rank = goal.PlayerPercentileBand is { } band
                ? goal.PlayerInTopRank == true ? $"Top rank · {band}th percentile" : $"{band}th percentile"
                : "";

            string timeNote;
            if (goal.Rewarded)
                timeNote = goal.RewardAmount is { } reward
                    ? $"Reward collected · {reward:N0} cr" + (goal.RewardBonus is { } bonus and > 0 ? $" +{bonus:N0} cr bonus" : "")
                    : "Reward collected";
            else if (goal.IsComplete)
                timeNote = "Goal complete";
            else if (goal.TimeLeft(now) is { } left)
                timeNote = left <= TimeSpan.Zero ? "Expired" : $"{FormatTimeLeft(left)} remaining";
            else
                timeNote = "";

            Goals.Add(new CommunityGoalLine(
                Title: goal.Title,
                Where: where,
                Tier: tier,
                Contribution: contribution,
                Rank: rank,
                HasRank: !string.IsNullOrWhiteSpace(rank),
                TimeNote: timeNote,
                HasTimeNote: !string.IsNullOrWhiteSpace(timeNote),
                Joined: goal.Joined ? "Joined" : "",
                HasJoined: goal.Joined,
                Rewarded: goal.Rewarded));
        }
    }

    public override void Reset()
    {
        _signature = "";
        Goals.Clear();
        Context.Host.CommunityGoals.Clear();
    }

    private static string FormatTimeLeft(TimeSpan left)
    {
        if (left.TotalDays >= 1) return $"{(int)left.TotalDays}d {left.Hours}h";
        if (left.TotalHours >= 1) return $"{(int)left.TotalHours}h {left.Minutes}m";
        return $"{(int)left.TotalMinutes}m";
    }
}

public sealed record CommunityGoalLine(
    string Title, string Where, string Tier, string Contribution, string Rank, bool HasRank,
    string TimeNote, bool HasTimeNote, string Joined, bool HasJoined, bool Rewarded);
