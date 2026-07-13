using System.Collections.ObjectModel;
using System.Linq;
using EDNexus.Core.Odyssey;
using EDNexus.Core.Settings;
using EDNexus.Core.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EDNexus.App.ViewModels;

/// <summary>
/// The Odyssey (on-foot) half of the Engineering card: pin a suit or weapon grade upgrade and see the
/// cumulative material checklist (with real "need N · have M" counts and a credit total, unlike the
/// ship side's deferred quantities) plus the modifications it can then take, each with its engineer.
/// </summary>
public sealed partial class EngineeringCardViewModel
{
    private readonly OdysseyCatalog _odyssey = OdysseyCatalog.Default;
    private string? _onFootKind;   // "suit" or "weapon"
    private string? _onFootId;
    private int _onFootGrade;
    private string _onFootSignature = "";

    private void InitializeOnFoot(EngineeringSettings pin)
    {
        foreach (var suit in _odyssey.Suits.Where(s => s.IsUpgradeable))
            OnFootOptions.Add(new OnFootOption("suit", suit.Id, $"Suit — {suit.Name}"));
        foreach (var weapon in _odyssey.Weapons)
            OnFootOptions.Add(new OnFootOption("weapon", weapon.Id, $"Weapon — {weapon.Name}"));

        _onFootKind = pin.PinnedOnFootKind;
        _onFootId = pin.PinnedOnFootId;
        _onFootGrade = pin.PinnedOnFootGrade is >= 1 and <= 5 ? pin.PinnedOnFootGrade : 5;

        SelectedOnFootOption = OnFootOptions.FirstOrDefault(o => o.Kind == _onFootKind && o.Id == _onFootId)
            ?? OnFootOptions.FirstOrDefault();
        SelectedOnFootGrade = _onFootGrade;
    }

    // --- Picker (shown when nothing is pinned) ---
    public ObservableCollection<OnFootOption> OnFootOptions { get; } = new();
    public int[] OnFootGradeChoices { get; } = { 1, 2, 3, 4, 5 };

    [ObservableProperty] private OnFootOption? _selectedOnFootOption;
    [ObservableProperty] private int _selectedOnFootGrade = 5;

    // --- Pinned goal ---
    [ObservableProperty] private bool _onFootHasPin;
    [ObservableProperty] private string _onFootPinnedTitle = "—";
    [ObservableProperty] private string _onFootPinnedGradeLabel = "";
    [ObservableProperty] private string _onFootCreditsTotal = "";
    [ObservableProperty] private string _onFootMaterialsSummary = "";

    public ObservableCollection<OnFootMaterialRow> OnFootMaterials { get; } = new();
    public ObservableCollection<ModRow> OnFootMods { get; } = new();

    /// <summary>Inverse of <see cref="OnFootHasPin"/> for the on-foot picker's visibility.</summary>
    public bool OnFootNoPin => !OnFootHasPin;

    partial void OnOnFootHasPinChanged(bool value) => OnPropertyChanged(nameof(OnFootNoPin));

    private void UpdateOnFoot(CommanderState s)
    {
        if (_onFootId is null || _onFootKind is null)
        {
            if (OnFootHasPin) { OnFootHasPin = false; OnFootMaterials.Clear(); OnFootMods.Clear(); _onFootSignature = ""; }
            return;
        }

        long credits; bool estimated;
        string title;
        int fromGrade, toGrade;
        System.Collections.Generic.IReadOnlyList<UpgradeRequirement> materials;
        System.Collections.Generic.IReadOnlyList<ModOption> mods;

        if (_onFootKind == "suit")
        {
            var plan = Context.Host.Engineering.BuildSuitUpgradePlan(_onFootId, _onFootGrade, s);
            if (plan is null) { ClearOnFootPin(); return; }
            title = plan.Suit.Name;
            fromGrade = plan.FromGrade; toGrade = plan.ToGrade;
            credits = plan.TotalCredits; estimated = plan.CreditsEstimated;
            materials = plan.Materials; mods = plan.Mods;
        }
        else if (_onFootKind == "weapon")
        {
            var plan = Context.Host.Engineering.BuildWeaponUpgradePlan(_onFootId, _onFootGrade, s);
            if (plan is null) { ClearOnFootPin(); return; }
            title = plan.Weapon.Name;
            fromGrade = plan.FromGrade; toGrade = plan.ToGrade;
            credits = plan.TotalCredits; estimated = plan.CreditsEstimated;
            materials = plan.Materials; mods = plan.Mods;
        }
        else
        {
            ClearOnFootPin();
            return;
        }

        var signature = _onFootKind + "|" + _onFootId + "|" + _onFootGrade + "|" + fromGrade
            + "|" + string.Join(",", materials.Select(m => $"{m.Symbol}:{m.Held}"))
            + "|" + string.Join(",", mods.Select(m => $"{m.Modification.Id}:{m.EngineerUnlocked}"));
        if (signature == _onFootSignature) return;
        _onFootSignature = signature;

        OnFootHasPin = true;
        OnFootPinnedTitle = title;
        OnFootPinnedGradeLabel = fromGrade == toGrade ? $"Grade {toGrade}" : $"Grade {fromGrade} → {toGrade}";
        OnFootCreditsTotal = estimated ? $"~{credits:N0} CR (estimated)" : $"{credits:N0} CR";

        OnFootMaterials.Clear();
        foreach (var m in materials.OrderBy(m => m.Satisfied).ThenBy(m => m.Name))
            OnFootMaterials.Add(new OnFootMaterialRow(m.Name, OnFootCategoryTag(m.Category), $"{m.Held:N0} / {m.Needed:N0}", m.Satisfied, m.Source));

        var have = materials.Count(m => m.Satisfied);
        OnFootMaterialsSummary = materials.Count == 0 ? "No further materials needed" : $"{have}/{materials.Count} materials met";

        OnFootMods.Clear();
        foreach (var m in mods)
        {
            var eng = m.Engineer;
            OnFootMods.Add(new ModRow(
                m.Modification.Name,
                m.Modification.Effect,
                eng?.Name ?? "—",
                eng is null ? "" : $"{eng.System} · {eng.Base}",
                eng?.Unlock ?? "",
                m.EngineerUnlocked));
        }
    }

    private void ClearOnFootPin()
    {
        if (OnFootHasPin) { OnFootHasPin = false; OnFootMaterials.Clear(); OnFootMods.Clear(); _onFootSignature = ""; }
    }

    [RelayCommand]
    private void PinOnFoot()
    {
        if (SelectedOnFootOption is not { } option) return;
        _onFootKind = option.Kind;
        _onFootId = option.Id;
        _onFootGrade = SelectedOnFootGrade is >= 1 and <= 5 ? SelectedOnFootGrade : 5;
        Context.SaveOnFootPin(_onFootKind, _onFootId, _onFootGrade);
        _onFootSignature = "";
    }

    [RelayCommand]
    private void UnpinOnFoot()
    {
        _onFootKind = null;
        _onFootId = null;
        Context.SaveOnFootPin(null, null, _onFootGrade);
        OnFootHasPin = false;
        OnFootMaterials.Clear();
        OnFootMods.Clear();
        _onFootSignature = "";
    }

    private void ResetOnFoot()
    {
        _onFootSignature = "";
        OnFootMaterials.Clear();
        OnFootMods.Clear();
    }

    private static string OnFootCategoryTag(string category) => category.ToLowerInvariant() switch
    {
        "item" => "GOOD",
        "component" => "COMP",
        "data" => "DATA",
        "consumable" => "CONS",
        _ => "?",
    };
}

/// <summary>A pickable suit or weapon in the on-foot picker combo.</summary>
public sealed record OnFootOption(string Kind, string Id, string Label);

/// <param name="CategoryTag">GOOD / COMP / DATA / CONS badge.</param>
/// <param name="NeededVsHeld">"3 / 12" style — held vs. exact quantity needed.</param>
/// <param name="Satisfied">True once held ≥ needed.</param>
/// <param name="Source">Where to farm it — shown as the row tooltip.</param>
public sealed record OnFootMaterialRow(string Name, string CategoryTag, string NeededVsHeld, bool Satisfied, string Source);

/// <summary>One modification available on the pinned suit/weapon, with its engineer resolved.</summary>
public sealed record ModRow(string Name, string Effect, string EngineerName, string EngineerLocation, string EngineerUnlock, bool EngineerUnlocked);
