using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EDNexus.Core.Missions;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>
/// Mission stacker: the kill missions currently held, grouped by the faction they target — a stack —
/// and the hand-ins waiting at each station.
/// </summary>
/// <remarks>
/// The point of the grouping is that one kill counts towards every mission in a stack at once, so
/// the card leads with the kills needed to <em>clear</em> a stack rather than the sum of its
/// missions, which is always the more alarming and less useful number.
/// </remarks>
public sealed partial class MissionsCardViewModel : CardViewModel
{
    private string _signature = "";

    public MissionsCardViewModel(DashboardContext context) : base(context, "missions", "MISSIONS", 452) { }

    [ObservableProperty] private bool _hasMissions;
    [ObservableProperty] private string _missionSummary = "";
    [ObservableProperty] private string _capNote = "";

    /// <summary>Inverse of <see cref="HasMissions"/>, for the empty-state hint's visibility.</summary>
    public bool NoMissions => !HasMissions;

    partial void OnHasMissionsChanged(bool value) => OnPropertyChanged(nameof(NoMissions));

    public ObservableCollection<StackLine> Stacks { get; } = new();
    public ObservableCollection<TurnInLine> TurnIns { get; } = new();

    public override void Update(CommanderState s)
    {
        var tracker = Context.Host.Missions;
        var active = tracker.Active;

        if (active.Count == 0)
        {
            if (HasMissions) { HasMissions = false; Stacks.Clear(); TurnIns.Clear(); _signature = ""; }
            return;
        }

        var stacks = tracker.Stacks;
        var turnIns = tracker.TurnIns;

        // Rebuild only when something actually moved: the dashboard ticks four times a second.
        var signature = string.Join("|", active.Select(m => $"{m.MissionId}:{m.ReadyToTurnIn}:{m.DestinationStation}"))
            + "#" + string.Join("|", stacks.Select(st => $"{st.TargetFaction}:{tracker.KillsLoggedFor(st.TargetFaction)}"));
        if (signature == _signature) return;
        _signature = signature;

        HasMissions = true;

        var stackNote = stacks.Count == 1 ? "1 stack" : $"{stacks.Count} stacks";
        MissionSummary = $"{active.Count} held · {stackNote} · {active.Sum(m => m.Reward):N0} cr on the board";
        CapNote = $"{active.Count} of {MissionTracker.MissionCap} mission slots used";

        Stacks.Clear();
        foreach (var stack in stacks)
        {
            var logged = tracker.KillsLoggedFor(stack.TargetFaction);
            Stacks.Add(new StackLine(
                Target: stack.TargetFaction,
                Subtitle: $"{stack.TargetType ?? "Targets"} · {stack.Missions.Count} missions from {stack.GiverFactions.Count} factions",
                Kills: $"{stack.KillsToClear:N0} kills to clear",
                // The sum is what the missions add up to; killing once ticks them all, so it is
                // shown as context rather than as the job.
                KillsNote: stack.Missions.Count > 1 ? $"({stack.TotalKills:N0} across the stack)" : "",
                Reward: $"{stack.TotalReward:N0} cr",
                Givers: string.Join(", ", stack.GiverFactions),
                Logged: logged > 0 ? $"{logged:N0} kills logged since EDNexus started" : "",
                HasLogged: logged > 0,
                ReadyNote: stack.ReadyCount > 0 ? $"{stack.ReadyCount} ready to hand in" : "",
                HasReady: stack.ReadyCount > 0));
        }

        TurnIns.Clear();
        foreach (var group in turnIns)
        {
            var where = string.IsNullOrWhiteSpace(group.Station)
                ? group.System ?? "Unknown"
                : $"{group.Station}{(string.IsNullOrWhiteSpace(group.System) ? "" : $" · {group.System}")}";
            TurnIns.Add(new TurnInLine(
                Where: where,
                Detail: $"{group.Missions.Count} mission{(group.Missions.Count == 1 ? "" : "s")} · {group.TotalReward:N0} cr",
                AllReady: group.AllReady,
                ReadyNote: group.AllReady ? "all ready" : $"{group.Missions.Count(m => m.ReadyToTurnIn)} ready"));
        }
    }

    public override void Reset()
    {
        _signature = "";
        Stacks.Clear();
        TurnIns.Clear();
        Context.Host.Missions.Clear();
    }
}

/// <param name="KillsNote">The stack's summed kill count, shown only when stacking actually helps.</param>
/// <param name="Logged">Running bounty tally against the target — kills seen, not mission progress.</param>
public sealed record StackLine(
    string Target, string Subtitle, string Kills, string KillsNote, string Reward,
    string Givers, string Logged, bool HasLogged, string ReadyNote, bool HasReady);

public sealed record TurnInLine(string Where, string Detail, bool AllReady, string ReadyNote);
