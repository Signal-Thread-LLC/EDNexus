using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDNexus.Core.Engineering;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>
/// The unlock path for every ship engineer: where the commander stands with each, and the one
/// concrete thing to do next. Ordered so the actionable ones lead and the finished ones sink,
/// because "which engineer should I go and work on" is the question this answers.
/// </summary>
public sealed partial class EngineersCardViewModel : CardViewModel
{
    private string _signature = "";

    public EngineersCardViewModel(DashboardContext context) : base(context, "engineers", "ENGINEERS", 452) { }

    /// <summary>Driven entirely by <c>EngineerProgress</c>; no sample source of its own.</summary>
    public override bool CanRandomize => false;

    [ObservableProperty] private string _summary = "—";
    [ObservableProperty] private bool _hideFinished = true;

    /// <summary>Engineers with the commander's standing, most actionable first.</summary>
    public ObservableCollection<EngineerRow> Engineers { get; } = new();

    partial void OnHideFinishedChanged(bool value) => _signature = "";

    [RelayCommand]
    private void ToggleFinished() => HideFinished = !HideFinished;

    public override void Update(CommanderState s)
    {
        var standings = Context.Host.Engineering.Standings();

        var unlocked = standings.Count(x => x.IsUnlocked);
        var maxed = standings.Count(x => x.IsMaxed);
        Summary = $"{unlocked} of {standings.Count} unlocked · {maxed} at grade 5";

        var shown = HideFinished ? standings.Where(x => !x.IsMaxed).ToList() : standings;

        var signature = HideFinished + "|" + string.Join("|",
            shown.Select(x => $"{x.Engineer.Id}:{x.Status}:{x.Rank}:{x.RankProgress:0.##}"));
        if (signature == _signature) return;
        _signature = signature;

        Engineers.Clear();
        foreach (var x in shown)
            Engineers.Add(new EngineerRow(
                x.Engineer.Name,
                x.StatusLabel,
                x.Engineer.Location,
                x.NextStep,
                x.Progress,
                x.IsUnlocked,
                x.BlockedBy is not null,
                x.Engineer.Top.Count > 0 ? "G5: " + string.Join(", ", x.Engineer.Top) : ""));
    }

    public override void Reset()
    {
        _signature = "";
        Engineers.Clear();
    }
}

/// <summary>
/// One engineer row: the status pill, where to find them, and the single next action.
/// </summary>
/// <param name="Status">"UNKNOWN" / "KNOWN" / "INVITED" / "G1".."G5".</param>
/// <param name="Blocked">True when a referral from another engineer is what's missing.</param>
/// <param name="TopGrades">Which specialities this engineer takes to grade 5, if any.</param>
public sealed record EngineerRow(
    string Name, string Status, string Location, string NextStep,
    double Progress, bool Unlocked, bool Blocked, string TopGrades);
