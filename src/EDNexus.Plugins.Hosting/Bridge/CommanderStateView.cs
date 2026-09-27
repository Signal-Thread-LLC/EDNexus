using System.Collections.Concurrent;
using System.Collections.Frozen;
using EDNexus.Core.Journal;
using EDNexus.Core.State;
using EDNexus.Plugins.Abstractions;

namespace EDNexus.Plugins.Hosting.Bridge;

/// <summary>
/// Keeps an immutable <see cref="CommanderStateSnapshot"/> of the engine's state as of the last
/// completed journal event, for every plugin attached to one bridge.
/// </summary>
/// <remarks>
/// <para>
/// Plugins read state from their own threads, while <c>StateTracker</c> mutates
/// <see cref="CommanderState"/> on the journal thread — rebuilding inventories as <c>Clear()</c>
/// plus adds, and changing related scalars (system, body) one property at a time. Reading the live
/// object from a plugin thread could therefore observe an event half-applied. Instead, the
/// snapshot is rebuilt on the journal thread from <see cref="JournalEventBus.SubscribeCompleted"/>,
/// once every engine handler has finished with the entry, and published with a single volatile
/// write. Readers only ever see a whole snapshot.
/// </para>
/// <para>
/// Collections are re-frozen only when <see cref="CommanderState.CargoChanged"/> or
/// <see cref="CommanderState.MaterialsChanged"/> fired during the event (observed through read-only
/// subscriptions); otherwise the previous frozen copies are reused, so the per-event cost is a
/// handful of scalar reads.
/// </para>
/// <para>
/// The first snapshot is taken at construction. Build the bridge before the engine starts pumping
/// (or accept that it is corrected by the next event).
/// </para>
/// </remarks>
internal sealed class CommanderStatePublisher : IDisposable
{
    private readonly CommanderState _state;
    private readonly IDisposable _hook;
    private CommanderStateSnapshot _current;
    private IReadOnlyDictionary<string, int> _cargo;
    private IReadOnlyDictionary<string, int> _raw;
    private IReadOnlyDictionary<string, int> _manufactured;
    private IReadOnlyDictionary<string, int> _encoded;
    private int _cargoDirty;
    private int _materialsDirty;

    public CommanderStatePublisher(JournalEventBus bus, CommanderState state)
    {
        _state = state;
        _cargo = CommanderStateSnapshot.Freeze(state.Cargo);
        _raw = CommanderStateSnapshot.Freeze(state.Materials.Raw);
        _manufactured = CommanderStateSnapshot.Freeze(state.Materials.Manufactured);
        _encoded = CommanderStateSnapshot.Freeze(state.Materials.Encoded);
        _current = Build();

        state.CargoChanged += MarkCargoDirty;
        state.MaterialsChanged += MarkMaterialsDirty;
        _hook = bus.SubscribeCompleted(_ => Publish());
    }

    /// <summary>The snapshot as of the last completed event.</summary>
    public CommanderStateSnapshot Current => Volatile.Read(ref _current);

    private void MarkCargoDirty() => Volatile.Write(ref _cargoDirty, 1);

    private void MarkMaterialsDirty() => Volatile.Write(ref _materialsDirty, 1);

    /// <summary>Runs on the journal thread after every engine handler has seen the entry.</summary>
    private void Publish()
    {
        if (Interlocked.Exchange(ref _cargoDirty, 0) == 1)
            _cargo = CommanderStateSnapshot.Freeze(_state.Cargo);
        if (Interlocked.Exchange(ref _materialsDirty, 0) == 1)
        {
            _raw = CommanderStateSnapshot.Freeze(_state.Materials.Raw);
            _manufactured = CommanderStateSnapshot.Freeze(_state.Materials.Manufactured);
            _encoded = CommanderStateSnapshot.Freeze(_state.Materials.Encoded);
        }
        Volatile.Write(ref _current, Build());
    }

    private CommanderStateSnapshot Build() => new()
    {
        Name = _state.Name,
        Balance = _state.Balance,
        Ship = _state.Ship,
        ShipName = _state.ShipName,
        StarSystem = _state.StarSystem,
        Body = _state.Body,
        Docked = _state.Docked,
        StationDisplayName = _state.StationDisplayName,
        LastUpdated = _state.LastUpdated,
        Cargo = _cargo,
        RawMaterials = _raw,
        ManufacturedMaterials = _manufactured,
        EncodedMaterials = _encoded,
    };

    public void Dispose()
    {
        _hook.Dispose();
        _state.CargoChanged -= MarkCargoDirty;
        _state.MaterialsChanged -= MarkMaterialsDirty;
    }
}

/// <summary>
/// The <see cref="IReadOnlyCommanderState"/> a plugin with the <c>state</c> capability sees. Every
/// member reads the bridge's latest published <see cref="CommanderStateSnapshot"/> — state as of
/// the last completed journal event. There is no setter and no reference to the live
/// <see cref="CommanderState"/>, so <c>StateTracker</c> stays the only writer. Separate property
/// reads may straddle an event; <see cref="Snapshot"/> returns one consistent snapshot.
/// </summary>
/// <param name="publisher">The bridge's snapshot source. Dropped on <see cref="Revoke"/>.</param>
/// <param name="hidden">
/// While this returns true the view reads as an unknown commander (see
/// <see cref="PluginBridgeOptions.IsSimulated"/>).
/// </param>
internal sealed class CommanderStateView(CommanderStatePublisher publisher, Func<bool> hidden) : IReadOnlyCommanderState
{
    private CommanderStatePublisher? _publisher = publisher;

    private CommanderStateSnapshot Current
    {
        get
        {
            var source = Volatile.Read(ref _publisher);
            return source is null || hidden() ? CommanderStateSnapshot.Unknown : source.Current;
        }
    }

    /// <summary>
    /// Cuts the view off from the engine (on session dispose): from then on it reads as an unknown
    /// commander, and a plugin that kept hold of it no longer pins the engine's object graph.
    /// </summary>
    public void Revoke() => Volatile.Write(ref _publisher, null);

    public string? Name => Current.Name;
    public long Balance => Current.Balance;
    public string? Ship => Current.Ship;
    public string? ShipName => Current.ShipName;
    public string? StarSystem => Current.StarSystem;
    public string? Body => Current.Body;
    public bool Docked => Current.Docked;
    public string? StationDisplayName => Current.StationDisplayName;
    public DateTimeOffset LastUpdated => Current.LastUpdated;
    public IReadOnlyDictionary<string, int> Cargo => Current.Cargo;
    public IReadOnlyDictionary<string, int> RawMaterials => Current.RawMaterials;
    public IReadOnlyDictionary<string, int> ManufacturedMaterials => Current.ManufacturedMaterials;
    public IReadOnlyDictionary<string, int> EncodedMaterials => Current.EncodedMaterials;

    public IReadOnlyCommanderState Snapshot() => Current;
}

/// <summary>
/// An immutable point-in-time copy of <see cref="CommanderState"/>. The collections are
/// <see cref="FrozenDictionary{TKey,TValue}"/> copies, which reject mutation even through a cast to
/// <see cref="IDictionary{TKey,TValue}"/>.
/// </summary>
internal sealed class CommanderStateSnapshot : IReadOnlyCommanderState
{
    private static readonly IReadOnlyDictionary<string, int> Empty =
        FrozenDictionary<string, int>.Empty.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The snapshot of a commander nothing is known about.</summary>
    public static CommanderStateSnapshot Unknown { get; } = new();

    public string? Name { get; internal init; }
    public long Balance { get; internal init; }
    public string? Ship { get; internal init; }
    public string? ShipName { get; internal init; }
    public string? StarSystem { get; internal init; }
    public string? Body { get; internal init; }
    public bool Docked { get; internal init; }
    public string? StationDisplayName { get; internal init; }
    public DateTimeOffset LastUpdated { get; internal init; }
    public IReadOnlyDictionary<string, int> Cargo { get; internal init; } = Empty;
    public IReadOnlyDictionary<string, int> RawMaterials { get; internal init; } = Empty;
    public IReadOnlyDictionary<string, int> ManufacturedMaterials { get; internal init; } = Empty;
    public IReadOnlyDictionary<string, int> EncodedMaterials { get; internal init; } = Empty;

    public IReadOnlyCommanderState Snapshot() => this;

    /// <summary>
    /// A frozen copy of an engine inventory. Only consistent when called on the journal thread
    /// between events (as <see cref="CommanderStatePublisher"/> does): mid-event, a
    /// <c>Clear()</c>-and-refill rebuild can be observed half done.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Freeze(ConcurrentDictionary<string, int> live)
        => live.IsEmpty ? Empty : live.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
}
