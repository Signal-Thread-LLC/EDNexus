using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EDNexus.Core.Ranks;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>
/// Pilot ranks: one row per ladder showing the current rank name and how far it is to the next tier.
/// </summary>
/// <remarks>
/// All five ranks come off the same journal events, so the card always shows all five — a rank the
/// commander has never touched sits at the bottom of its ladder rather than disappearing, which is
/// the honest reading of "no progress yet".
/// </remarks>
public sealed partial class RanksCardViewModel : CardViewModel
{
    private string _signature = "";

    public RanksCardViewModel(DashboardContext context) : base(context, "ranks", "RANKS", 452) { }

    [ObservableProperty] private string _summary = "";

    /// <summary>One row per tracked ladder, in ladder order.</summary>
    public ObservableCollection<RankRow> Ranks { get; } = new();

    public override void Update(CommanderState s)
    {
        var all = Context.Host.Ranks.All;

        // The dashboard ticks four times a second; only rebuild when something actually moved.
        var signature = string.Join("|", all.Select(r => $"{r.Kind}:{r.Index}:{r.Percent}"));
        if (signature == _signature) return;
        _signature = signature;

        Ranks.Clear();
        foreach (var rank in all) Ranks.Add(new RankRow(rank));

        var elite = all.Count(r => r.IsElite);
        Summary = elite == 0
            ? "No Elite ranks yet"
            : elite == 1 ? "1 Elite rank" : $"{elite} Elite ranks";
    }

    public override void Reset()
    {
        _signature = "";
        Ranks.Clear();
    }
}

/// <summary>One rank ladder as the card renders it.</summary>
public sealed class RankRow
{
    public RankRow(RankProgress rank)
    {
        Label = rank.Label;
        Name = rank.Name;
        Fraction = rank.Fraction;
        IsElite = rank.IsElite;
        // At the top of the ladder the bar has nothing left to fill, so say so instead of showing 0%.
        Progress = rank.IsMaxed ? "max" : $"{rank.Percent}%";
        ShowBar = !rank.IsMaxed;
    }

    /// <summary>The rank track, e.g. "Combat".</summary>
    public string Label { get; }

    /// <summary>Current rank name, e.g. "Dangerous".</summary>
    public string Name { get; }

    /// <summary>Progress to the next tier as text, or "max" at the top of the ladder.</summary>
    public string Progress { get; }

    /// <summary>Progress as a 0–1 fraction for the bar.</summary>
    public double Fraction { get; }

    /// <summary>True once this rank is Elite or beyond, so the row can be highlighted.</summary>
    public bool IsElite { get; }

    /// <summary>False at the top of the ladder, where a progress bar would be meaningless.</summary>
    public bool ShowBar { get; }
}
