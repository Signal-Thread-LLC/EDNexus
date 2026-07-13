using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDNexus.Core.Engineering;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>
/// Engineering focus card. The commander pins one blueprint+grade (ship side) or one suit/weapon
/// grade-upgrade (Odyssey on-foot side, via the <see cref="OnFootMode"/> toggle) and the card collapses
/// the whole relevant catalogue down to that single goal: which engineer to visit (and whether it's
/// unlocked) and the material checklist, each row tooltip-annotated with where to farm it. This is the
/// antidote to being overwhelmed by every blueprint/upgrade at once.
/// </summary>
public sealed partial class EngineeringCardViewModel : CardViewModel
{
    private readonly EngineeringCatalog _catalog = EngineeringCatalog.Default;
    private string? _pinnedId;
    private int _pinnedGrade;
    private string _signature = "";

    public EngineeringCardViewModel(DashboardContext context) : base(context, "engineering", "ENGINEERING", 452)
    {
        foreach (var b in _catalog.Blueprints.OrderBy(b => b.Module).ThenBy(b => b.Name))
            Blueprints.Add(new BlueprintOption(b.Id, $"{b.Module} — {b.Name}", b.MaxGrade));

        var pin = context.GetEngineeringPin();
        _pinnedId = pin.PinnedBlueprintId;
        _pinnedGrade = pin.PinnedGrade is >= 1 and <= 5 ? pin.PinnedGrade : 5;
        _onFootMode = pin.OnFootMode;

        SelectedBlueprint = Blueprints.FirstOrDefault(o => o.Id == _pinnedId) ?? Blueprints.FirstOrDefault();
        SelectedGrade = _pinnedGrade;

        InitializeOnFoot(pin);
    }

    /// <summary>No dev-mode sample source of its own; held counts come from the Materials sampler.</summary>
    public override bool CanRandomize => false;

    [ObservableProperty] private bool _onFootMode;

    [RelayCommand]
    private void ToggleOnFootMode()
    {
        OnFootMode = !OnFootMode;
        Context.SaveEngineeringOnFootMode(OnFootMode);
    }

    // --- Picker (shown when nothing is pinned) ---
    public ObservableCollection<BlueprintOption> Blueprints { get; } = new();
    public int[] GradeChoices { get; } = { 1, 2, 3, 4, 5 };

    [ObservableProperty] private BlueprintOption? _selectedBlueprint;
    [ObservableProperty] private int _selectedGrade = 5;

    // --- Pinned goal ---
    [ObservableProperty] private bool _hasPin;
    [ObservableProperty] private string _pinnedTitle = "—";
    [ObservableProperty] private string _pinnedGradeLabel = "";
    [ObservableProperty] private string _engineerName = "—";
    [ObservableProperty] private string _engineerLocation = "";
    [ObservableProperty] private string _engineerUnlock = "";
    [ObservableProperty] private string _engineerStatus = "";
    [ObservableProperty] private bool _engineerUnlocked;
    [ObservableProperty] private string _otherEngineers = "";
    [ObservableProperty] private string _materialsSummary = "";

    private string _engineerSystem = "";

    public ObservableCollection<MaterialRow> Materials { get; } = new();

    /// <summary>Inverse of <see cref="HasPin"/> for the picker's visibility.</summary>
    public bool NoPin => !HasPin;

    partial void OnHasPinChanged(bool value) => OnPropertyChanged(nameof(NoPin));

    partial void OnSelectedBlueprintChanged(BlueprintOption? value)
    {
        // Keep the requested grade within the blueprint's range.
        if (value is not null && SelectedGrade > value.MaxGrade) SelectedGrade = value.MaxGrade;
    }

    public override void Update(CommanderState s)
    {
        UpdateOnFoot(s);

        if (_pinnedId is null)
        {
            if (HasPin) { HasPin = false; Materials.Clear(); _signature = ""; }
            return;
        }

        var plan = Context.Host.Engineering.BuildPlan(_pinnedId, _pinnedGrade, s);
        if (plan is null)
        {
            // Pinned id no longer in the catalogue — fall back to the picker.
            if (HasPin) { HasPin = false; Materials.Clear(); _signature = ""; }
            return;
        }

        var signature = _pinnedId + "|" + _pinnedGrade + "|" + (plan.Engineer?.Name ?? "") + "|" + plan.EngineerUnlocked
            + "|" + string.Join(",", plan.Materials.Select(m => $"{m.Symbol}:{m.Held}"));
        if (signature == _signature) return;
        _signature = signature;

        HasPin = true;
        PinnedTitle = $"{plan.Blueprint.Module} — {plan.Blueprint.Name}";
        PinnedGradeLabel = $"Grade {plan.Grade}";

        if (plan.Engineer is { } eng)
        {
            EngineerName = eng.Name;
            EngineerLocation = $"{eng.System} · {eng.Base}";
            EngineerUnlock = eng.Unlock;
            _engineerSystem = eng.System;
            EngineerUnlocked = plan.EngineerUnlocked;
            EngineerStatus = plan.EngineerUnlocked
                ? (Context.Host.Engineering.UnlockedRanks.TryGetValue(eng.Name, out var rk)
                    ? $"✓ Unlocked (rank {rk})" : "✓ Unlocked")
                : "Locked — hover for how to unlock";
        }
        else
        {
            EngineerName = "—";
            EngineerLocation = "";
            EngineerUnlock = "";
            _engineerSystem = "";
            EngineerUnlocked = false;
            EngineerStatus = "No engineer data for this grade";
        }

        OtherEngineers = plan.OtherEngineers.Count == 0
            ? ""
            : "Also offered by: " + string.Join(", ", plan.OtherEngineers.Select(e => $"{e.Name} ({e.System})"));

        Materials.Clear();
        foreach (var m in plan.Materials)
            Materials.Add(new MaterialRow(m.Name, CategoryTag(m.Category), m.Held.ToString("N0"), m.HasAny, m.Source));

        var have = plan.Materials.Count(m => m.HasAny);
        MaterialsSummary = $"{have}/{plan.Materials.Count} material types in stock";
    }

    [RelayCommand]
    private void Pin()
    {
        if (SelectedBlueprint is not { } option) return;
        _pinnedId = option.Id;
        _pinnedGrade = SelectedGrade is >= 1 and <= 5 ? SelectedGrade : option.MaxGrade;
        Context.SaveEngineeringPin(_pinnedId, _pinnedGrade);
        _signature = "";   // force the next Update to repopulate
    }

    [RelayCommand]
    private void Unpin()
    {
        _pinnedId = null;
        Context.SaveEngineeringPin(null, _pinnedGrade);
        HasPin = false;
        Materials.Clear();
        _signature = "";
    }

    [RelayCommand]
    private async Task CopyEngineerSystem()
    {
        if (_engineerSystem.Length > 0) await CopyToClipboardAsync(_engineerSystem);
    }

    public override void Reset()
    {
        _signature = "";
        Materials.Clear();
        ResetOnFoot();
    }

    private static string CategoryTag(string category) => category.ToLowerInvariant() switch
    {
        "raw" => "RAW",
        "manufactured" => "MANU",
        "encoded" => "ENC",
        _ => "?",
    };
}

/// <summary>A pickable blueprint in the picker combo. <see cref="MaxGrade"/> clamps the grade selector.</summary>
public sealed record BlueprintOption(string Id, string Label, int MaxGrade);

/// <param name="CategoryTag">RAW / MANU / ENC badge.</param>
/// <param name="HasAny">True when the commander holds at least one — until exact counts are bundled,
/// that is treated as "covered" and the row is not flagged short.</param>
/// <param name="Source">Where to farm it — shown as the row tooltip.</param>
public sealed record MaterialRow(string Name, string CategoryTag, string Held, bool HasAny, string Source);
