using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDNexus.Core;
using EDNexus.Core.Settings;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>
/// Ambient services a card may need beyond the per-tick state snapshot: the current engine host, a
/// developer-mode predicate, and a shared RNG for the offline sample services. The host is fetched
/// through a delegate rather than cached because "reset to live" rebuilds it — a card that held a
/// stale reference would keep reading the disposed engine.
/// </summary>
public sealed class DashboardContext
{
    private readonly Func<EngineHost> _host;
    private readonly Func<bool> _devEnabled;
    private readonly Func<EngineeringSettings> _getEngineeringPin;
    private readonly Action<string?, int> _saveEngineeringPin;

    public DashboardContext(
        Func<EngineHost> host,
        Func<bool> devEnabled,
        Random rng,
        Func<EngineeringSettings> getEngineeringPin,
        Action<string?, int> saveEngineeringPin)
    {
        _host = host;
        _devEnabled = devEnabled;
        Rng = rng;
        _getEngineeringPin = getEngineeringPin;
        _saveEngineeringPin = saveEngineeringPin;
    }

    /// <summary>The live engine host — always the current one, even after a reset-to-live rebuild.</summary>
    public EngineHost Host => _host();

    /// <summary>True while developer mode is active (cards then run against offline generators).</summary>
    public bool DevEnabled => _devEnabled();

    public Random Rng { get; }

    /// <summary>Read the persisted engineering pin (blueprint id + grade).</summary>
    public EngineeringSettings GetEngineeringPin() => _getEngineeringPin();

    /// <summary>Persist the engineering pin; a null blueprint id clears it.</summary>
    public void SaveEngineeringPin(string? blueprintId, int grade) => _saveEngineeringPin(blueprintId, grade);
}

/// <summary>
/// A single dashboard card. Each card owns its own slice of the commander state and refreshes itself
/// from a state snapshot on every tick, so the shell view model no longer has to know what any card
/// contains. Identity (<see cref="Id"/>), placement (<see cref="Width"/>), and the show/collapse flags
/// are the hooks the shell — and, later, a customisable layout — drive.
/// </summary>
public abstract partial class CardViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    protected DashboardContext Context { get; }

    protected CardViewModel(DashboardContext context, string id, string title, double width)
    {
        Context = context;
        Id = id;
        Title = title;
        _width = width;
    }

    /// <summary>Stable key, aligned with the dev-mode sample source keys (e.g. "location", "market").</summary>
    public string Id { get; }

    /// <summary>Header text shown on the card.</summary>
    public string Title { get; }

    /// <summary>Whether this card supports the dev-mode 🎲 reshuffle (only cards with a sample source do).</summary>
    public virtual bool CanRandomize => true;

    [ObservableProperty] private double _width;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _isCollapsed;

    /// <summary>Glyph for the collapse toggle, so the header needs no bool-to-text converter.</summary>
    public string CollapseGlyph => IsCollapsed ? "▸" : "▾";

    partial void OnIsCollapsedChanged(bool value) => OnPropertyChanged(nameof(CollapseGlyph));

    [RelayCommand]
    private void ToggleCollapse() => IsCollapsed = !IsCollapsed;

    /// <summary>Pull fresh values from the latest state snapshot. Called on the UI thread each tick.</summary>
    public abstract void Update(CommanderState state);

    /// <summary>Drop any cached diff/collection state so the next update rebuilds from scratch.</summary>
    public virtual void Reset() { }

    /// <summary>Copy text to the OS clipboard via the active desktop window.</summary>
    protected static async Task CopyToClipboardAsync(string text)
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
