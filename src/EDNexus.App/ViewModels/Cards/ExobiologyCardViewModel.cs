using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EDNexus.Core.Exobio;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>
/// Exobiology card. Leads with what matters on foot — what is on this body and how far the current
/// three-sample run has got — then the credits riding on the sampler and what the session has banked.
/// </summary>
public sealed partial class ExobiologyCardViewModel : CardViewModel
{
    /// <summary>Candidates listed on the card; the rest are long-shots not worth the space.</summary>
    private const int MaxPredictionLines = 6;

    private string _signature = "";
    private string _predictionSignature = "";

    public ExobiologyCardViewModel(DashboardContext context) : base(context, "exobio", "EXOBIOLOGY", 452) { }

    // --- "Bio signals here", from the FSS count or the richer DSS mapping. ---
    [ObservableProperty] private bool _hasBody;
    [ObservableProperty] private string _bodyName = "—";
    [ObservableProperty] private string _bodySignals = "";
    [ObservableProperty] private string _bodyGenera = "";
    [ObservableProperty] private string _bodyValue = "";
    [ObservableProperty] private bool _hasPredictions;
    [ObservableProperty] private string _predictionsHeader = "";

    /// <summary>The body was undiscovered when scanned — the heuristic for the 5× first-logged bonus.</summary>
    [ObservableProperty] private bool _isFirstDiscovery;

    /// <summary>Species the body's physics can grow, richest first, with how far apart to sample each.</summary>
    public ObservableCollection<PredictedBioLine> PredictedSpecies { get; } = new();

    // --- The run in progress. ---
    [ObservableProperty] private bool _hasScan;
    [ObservableProperty] private string _scanSpecies = "";
    [ObservableProperty] private string _scanProgress = "";
    [ObservableProperty] private double _scanFraction;
    [ObservableProperty] private string _scanValue = "";
    [ObservableProperty] private string _scanDistance = "";

    // --- Session tally. ---
    [ObservableProperty] private string _pendingValue = "0 cr";
    [ObservableProperty] private string _pendingSummary = "nothing sampled yet";
    [ObservableProperty] private string _soldValue = "0 cr";
    [ObservableProperty] private string _soldSummary = "";

    /// <summary>Completed samples waiting for a Vista Genomics terminal, most valuable first.</summary>
    public ObservableCollection<BioLine> Pending { get; } = new();

    /// <summary>True when there is nothing at all to show, so the card can explain itself.</summary>
    public bool NoExobio => !HasBody && !HasScan && Pending.Count == 0;

    partial void OnHasBodyChanged(bool value) => OnPropertyChanged(nameof(NoExobio));
    partial void OnHasScanChanged(bool value) => OnPropertyChanged(nameof(NoExobio));

    public override void Update(CommanderState s)
    {
        var exo = Context.Host.Exobiology;

        UpdateBody(exo.CurrentBody);
        UpdateScan(exo.ActiveScan);
        UpdateSession(exo.Session, exo.NewDiscoveries);
    }

    private void UpdateBody(BodyBioSignals? body)
    {
        if (body is null)
        {
            HasBody = false;
            IsFirstDiscovery = false;
            UpdatePredictions(Array.Empty<BioPrediction>());
            return;
        }

        HasBody = true;
        BodyName = body.BodyName;
        IsFirstDiscovery = body.IsUndiscovered;
        BodySignals = body.SignalCount == 1 ? "1 bio signal" : $"{body.SignalCount} bio signals";

        // An FSS pass gives a count and nothing else; only the DSS names what is down there.
        BodyGenera = body.Genera.Count > 0
            ? string.Join(" · ", body.Genera.Select(g => $"{g.Name} ({g.SampleDistanceMeters} m)"))
            : body.Predictions.Count > 0
                ? "Map the body (DSS) to confirm which of these are here"
                : "Map the body (DSS) to identify the genera";

        BodyValue = body.ValueRange is { } range
            ? $"{Money(range.Min)} – {Money(range.Max)}"
            : "";

        // Physics alone roughly doubles the genera actually present, so only call it "likely" once a DSS narrows it.
        PredictionsHeader = body.Genera.Count > 0 ? "LIKELY SPECIES · SAMPLE DISTANCE" : "POSSIBLE SPECIES · SAMPLE DISTANCE";
        UpdatePredictions(body.Predictions);
    }

    /// <summary>Rebuild the candidate list only when it actually changed, so the card doesn't flicker every tick.</summary>
    private void UpdatePredictions(IReadOnlyList<BioPrediction> predictions)
    {
        var shown = predictions.Take(MaxPredictionLines).ToList();
        HasPredictions = shown.Count > 0;

        var signature = string.Join("|", shown.Select(p => $"{p.Species.Symbol}:{p.EstimatedValue}"));
        if (signature == _predictionSignature) return;
        _predictionSignature = signature;

        PredictedSpecies.Clear();
        foreach (var p in shown)
            PredictedSpecies.Add(new PredictedBioLine(
                p.Species.Name,
                p.SampleDistanceMeters > 0 ? $"{p.SampleDistanceMeters} m" : "",
                Money(p.EstimatedValue),
                p.IsFirstDiscovery
                    ? $"{Money(p.BaseValue)} base × 5 if first logged"
                    : $"{Money(p.BaseValue)} base"));
    }

    private void UpdateScan(OrganicScan? scan)
    {
        if (scan is null)
        {
            HasScan = false;
            ScanDistance = "";
            return;
        }

        HasScan = true;
        ScanSpecies = scan.SpeciesName;
        ScanProgress = scan.Progress;
        ScanFraction = Math.Clamp(scan.Samples / 3.0, 0, 1);
        ScanValue = scan.Value > 0 ? $"{Money(scan.Value)} when complete" : "value unknown";
        ScanDistance = scan.SampleDistanceMeters > 0 ? $"{scan.SampleDistanceMeters} m between samples" : "";
    }

    private void UpdateSession(ExobiologySession session, int newDiscoveries)
    {
        PendingValue = Money(session.PendingValue);
        PendingSummary = session.Pending.Count switch
        {
            0 => "nothing sampled yet",
            1 => "1 sample to sell",
            var n => $"{n} samples to sell",
        };

        SoldValue = Money(session.SoldValue);
        SoldSummary = session.SoldCount == 0
            ? "none sold this session"
            : session.SoldBonus > 0
                ? $"{session.SoldCount} sold · {Money(session.SoldBonus)} first-logged bonus"
                : $"{session.SoldCount} sold this session";

        if (newDiscoveries > 0)
            SoldSummary += newDiscoveries == 1
                ? " · 1 new discovery"
                : $" · {newDiscoveries} new discoveries";

        var signature = string.Join("|", session.Pending.Select(p => $"{p.SpeciesName}:{p.BodyName}:{p.Value}"));
        if (signature == _signature) return;
        _signature = signature;

        Pending.Clear();
        foreach (var p in session.Pending)
            Pending.Add(new BioLine(p.SpeciesName, p.BodyName, Money(p.Value), Money(p.Value * 5)));
        OnPropertyChanged(nameof(NoExobio));
    }

    public override void Reset()
    {
        _signature = "";
        _predictionSignature = "";
        Pending.Clear();
        PredictedSpecies.Clear();
        HasBody = false;
        HasScan = false;
        HasPredictions = false;
        IsFirstDiscovery = false;
    }

    /// <summary>Exobiology payouts run to tens of millions, so abbreviate rather than print every digit.</summary>
    private static string Money(long credits) => credits switch
    {
        0 => "0 cr",
        >= 1_000_000 => $"{credits / 1_000_000.0:0.#}M cr",
        >= 1_000 => $"{credits / 1_000.0:0.#}k cr",
        _ => $"{credits:N0} cr",
    };
}

/// <summary>
/// One predicted species on the card: its name, how far apart to take its samples, and the expected
/// payout — five times the base on an undiscovered body — with the base spelled out in <see cref="PayoutTip"/>.
/// </summary>
public sealed record PredictedBioLine(string Name, string Distance, string Payout, string PayoutTip);

/// <summary>
/// One analysed sample waiting to be sold: what it is, where it came from, and what it pays —
/// with <see cref="FirstLogged"/> showing the five-times figure if nobody has logged it before.
/// </summary>
public sealed record BioLine(string Species, string Body, string Value, string FirstLogged);
