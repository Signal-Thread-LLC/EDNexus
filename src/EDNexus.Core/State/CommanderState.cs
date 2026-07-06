using System.Collections.Concurrent;

namespace EDNexus.Core.State;

/// <summary>
/// The single live picture of the commander that every feature module reads from.
/// Scalar properties raise <see cref="ObservableObject.PropertyChanged"/>; the collections
/// raise their own change events since dictionaries don't notify on their own.
/// </summary>
public sealed class CommanderState : ObservableObject
{
    private string? _name;
    public string? Name { get => _name; set => Set(ref _name, value); }

    private long _balance;
    public long Balance { get => _balance; set => Set(ref _balance, value); }

    private string? _ship;
    public string? Ship { get => _ship; set => Set(ref _ship, value); }

    private string? _shipName;
    public string? ShipName { get => _shipName; set => Set(ref _shipName, value); }

    private string? _shipIdent;
    public string? ShipIdent { get => _shipIdent; set => Set(ref _shipIdent, value); }

    private string? _starSystem;
    public string? StarSystem { get => _starSystem; set => Set(ref _starSystem, value); }

    private string? _body;
    public string? Body { get => _body; set => Set(ref _body, value); }

    private bool _docked;
    public bool Docked { get => _docked; set => Set(ref _docked, value); }

    private string? _stationName;
    public string? StationName { get => _stationName; set => Set(ref _stationName, value); }

    private double _fuelMain;
    public double FuelMain { get => _fuelMain; set => Set(ref _fuelMain, value); }

    private double _fuelCapacity;
    public double FuelCapacity { get => _fuelCapacity; set => Set(ref _fuelCapacity, value); }

    private double _cargoTons;
    public double CargoTons { get => _cargoTons; set => Set(ref _cargoTons, value); }

    private DateTimeOffset _lastUpdated;
    public DateTimeOffset LastUpdated { get => _lastUpdated; set => Set(ref _lastUpdated, value); }

    /// <summary>Current cargo hold: commodity name → tons.</summary>
    public ConcurrentDictionary<string, int> Cargo { get; } = new(StringComparer.OrdinalIgnoreCase);

    public MaterialsInventory Materials { get; } = new();

    public event Action? CargoChanged;
    public event Action? MaterialsChanged;

    public void RaiseCargoChanged() => CargoChanged?.Invoke();
    public void RaiseMaterialsChanged() => MaterialsChanged?.Invoke();
}

/// <summary>Engineering materials split into the game's three categories.</summary>
public sealed class MaterialsInventory
{
    public ConcurrentDictionary<string, int> Raw { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, int> Manufactured { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, int> Encoded { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int TotalCount => Raw.Values.Sum() + Manufactured.Values.Sum() + Encoded.Values.Sum();
}
