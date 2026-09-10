using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    // The open War Board window, so a second click focuses it rather than stacking windows.
    private Window? _warBoard;

    public MissionsCardViewModel(DashboardContext context) : base(context, "missions", "MISSIONS", 452) { }

    [ObservableProperty] private bool _hasMissions;
    [ObservableProperty] private string _missionSummary = "";
    [ObservableProperty] private string _capNote = "";
    [ObservableProperty] private string _totalPayout = "";

    /// <summary>Inverse of <see cref="HasMissions"/>, for the empty-state hint's visibility.</summary>
    public bool NoMissions => !HasMissions;

    partial void OnHasMissionsChanged(bool value) => OnPropertyChanged(nameof(NoMissions));

    public ObservableCollection<StackLine> Stacks { get; } = new();
    public ObservableCollection<TurnInLine> TurnIns { get; } = new();

    /// <summary>Every active mission individually, for the War Board — the card itself only shows stacks.</summary>
    public ObservableCollection<MissionLine> AllMissions { get; } = new();

    public override void Update(CommanderState s)
    {
        var tracker = Context.Host.Missions;
        var active = tracker.Active;

        if (active.Count == 0)
        {
            if (HasMissions)
            {
                HasMissions = false;
                Stacks.Clear();
                TurnIns.Clear();
                AllMissions.Clear();
                TotalPayout = "";
                _signature = "";
            }
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

        var now = DateTimeOffset.UtcNow;
        var totalReward = active.Sum(m => m.Reward);

        var stackNote = stacks.Count == 1 ? "1 stack" : $"{stacks.Count} stacks";
        MissionSummary = $"{active.Count} held · {stackNote} · {totalReward:N0} cr on the board";
        CapNote = $"{active.Count} of {MissionTracker.MissionCap} mission slots used";
        TotalPayout = $"{totalReward:N0} cr across {active.Count} active mission{(active.Count == 1 ? "" : "s")}";

        Stacks.Clear();
        foreach (var stack in stacks)
        {
            var logged = tracker.KillsLoggedFor(stack.TargetFaction);
            var expiry = SoonestExpiry(stack.Missions, now);
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
                HasReady: stack.ReadyCount > 0,
                ExpiryNote: expiry.Note,
                HasExpiryNote: expiry.HasNote,
                IsUrgent: expiry.IsUrgent));
        }

        TurnIns.Clear();
        foreach (var group in turnIns)
        {
            var where = string.IsNullOrWhiteSpace(group.Station)
                ? group.System ?? "Unknown"
                : $"{group.Station}{(string.IsNullOrWhiteSpace(group.System) ? "" : $" · {group.System}")}";
            var expiry = SoonestExpiry(group.Missions, now);
            TurnIns.Add(new TurnInLine(
                Where: where,
                Detail: $"{group.Missions.Count} mission{(group.Missions.Count == 1 ? "" : "s")} · {group.TotalReward:N0} cr",
                AllReady: group.AllReady,
                ReadyNote: group.AllReady ? "all ready" : $"{group.Missions.Count(m => m.ReadyToTurnIn)} ready",
                ExpiryNote: expiry.Note,
                HasExpiryNote: expiry.HasNote,
                IsUrgent: expiry.IsUrgent));
        }

        AllMissions.Clear();
        foreach (var mission in active
                     .OrderByDescending(m => m.ReadyToTurnIn)
                     .ThenBy(m => m.Expiry ?? DateTimeOffset.MaxValue))
        {
            var expiry = SoonestExpiry(new[] { mission }, now);
            var target = !string.IsNullOrWhiteSpace(mission.TargetFaction)
                ? mission.TargetFaction!
                : mission.TargetType ?? "—";

            AllMissions.Add(new MissionLine(
                MissionId: mission.MissionId,
                Title: mission.Title,
                GiverFaction: string.IsNullOrWhiteSpace(mission.GiverFaction) ? "Unknown faction" : mission.GiverFaction,
                Target: target,
                Reward: $"{mission.Reward:N0} cr",
                ExpiryNote: expiry.Note,
                HasExpiryNote: expiry.HasNote,
                IsUrgent: expiry.IsUrgent,
                ReadyToTurnIn: mission.ReadyToTurnIn));
        }
    }

    public override void Reset()
    {
        _signature = "";
        Stacks.Clear();
        TurnIns.Clear();
        AllMissions.Clear();
        TotalPayout = "";
        Context.Host.Missions.Clear();
    }

    /// <summary>
    /// Open the War Board — every active mission on its own, with the expiry countdown and payout
    /// total. A second open focuses the window already up rather than stacking another.
    /// </summary>
    [RelayCommand]
    private void OpenWarBoard()
    {
        if (_warBoard is not null)
        {
            _warBoard.Activate();
            return;
        }

        var owner = (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        var board = new Views.WarBoardWindow { DataContext = this };
        board.Closed += (_, _) => _warBoard = null;
        _warBoard = board;

        // Owned by the dashboard: the app's shutdown mode closes on the last window, so an unowned
        // War Board left open would keep EDNexus running after the main window had gone.
        if (owner is not null) board.Show(owner);
        else board.Show();
    }

    /// <summary>
    /// The nearest deadline among a group of missions, formatted for display. <c>HasNote</c> is only
    /// true for the non-urgent case — the two are shown by separate, mutually exclusive elements in
    /// the UI (dim vs. highlighted), so this keeps the XAML to plain boolean bindings.
    /// </summary>
    private static (string Note, bool HasNote, bool IsUrgent) SoonestExpiry(IReadOnlyList<Mission> missions, DateTimeOffset now)
    {
        var soonest = missions
            .Where(m => m.Expiry is not null)
            .OrderBy(m => m.Expiry)
            .FirstOrDefault();
        if (soonest is null) return ("", false, false);

        var left = soonest.TimeLeft(now);
        if (left is not { } l) return ("", false, false);
        if (l <= TimeSpan.Zero) return ("Expired", false, true);

        var urgent = l <= TimeLeftFormatter.UrgentThreshold;
        return ($"{TimeLeftFormatter.Format(l)} left", !urgent, urgent);
    }
}

/// <param name="KillsNote">The stack's summed kill count, shown only when stacking actually helps.</param>
/// <param name="Logged">Running bounty tally against the target — kills seen, not mission progress.</param>
/// <param name="ExpiryNote">Formatted time left on the stack's soonest deadline, or blank when none has one.</param>
/// <param name="HasExpiryNote">True when at least one mission in the stack has a deadline.</param>
/// <param name="IsUrgent">True when the soonest deadline is under the near-expiry threshold.</param>
public sealed record StackLine(
    string Target, string Subtitle, string Kills, string KillsNote, string Reward,
    string Givers, string Logged, bool HasLogged, string ReadyNote, bool HasReady,
    string ExpiryNote, bool HasExpiryNote, bool IsUrgent);

/// <param name="ExpiryNote">Formatted time left on the group's soonest deadline, or blank when none has one.</param>
/// <param name="HasExpiryNote">True when at least one mission in the group has a deadline.</param>
/// <param name="IsUrgent">True when the soonest deadline is under the near-expiry threshold.</param>
public sealed record TurnInLine(
    string Where, string Detail, bool AllReady, string ReadyNote,
    string ExpiryNote, bool HasExpiryNote, bool IsUrgent);

/// <summary>One held mission on its own — what the War Board lists, as opposed to the card's stacks.</summary>
/// <param name="ExpiryNote">Formatted time left on this mission's deadline, or blank when it has none.</param>
/// <param name="HasExpiryNote">True when the mission has a deadline.</param>
/// <param name="IsUrgent">True when the deadline is under the near-expiry threshold.</param>
public sealed record MissionLine(
    long MissionId, string Title, string GiverFaction, string Target, string Reward,
    string ExpiryNote, bool HasExpiryNote, bool IsUrgent, bool ReadyToTurnIn);
