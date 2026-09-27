using System.Collections.Concurrent;
using System.Collections.Frozen;
using EDNexus.Core.State;
using EDNexus.Plugins.Abstractions;

namespace EDNexus.Plugins.Hosting.Bridge;

/// <summary>
/// The live <see cref="IReadOnlyCommanderState"/> a plugin with the <c>state</c> capability sees.
/// Scalars are read straight off <see cref="CommanderState"/> (strings and value types, so nothing
/// mutable escapes); collections are copied into frozen dictionaries on every read. There is no
/// setter and no path back to the live object — <c>StateTracker</c> stays the only writer.
/// </summary>
/// <param name="state">The engine's state. Held privately and only ever read.</param>
/// <param name="hidden">
/// While this returns true the view reads as an empty commander (see
/// <see cref="PluginBridgeOptions.IsSimulated"/>).
/// </param>
internal sealed class CommanderStateView(CommanderState state, Func<bool> hidden) : IReadOnlyCommanderState
{
    private readonly CommanderState _state = state;
    private readonly Func<bool> _hidden = hidden;

    private CommanderState? Source => _hidden() ? null : _state;

    public string? Name => Source?.Name;
    public long Balance => Source?.Balance ?? 0;
    public string? Ship => Source?.Ship;
    public string? ShipName => Source?.ShipName;
    public string? StarSystem => Source?.StarSystem;
    public string? Body => Source?.Body;
    public bool Docked => Source?.Docked ?? false;
    public string? StationDisplayName => Source?.StationDisplayName;
    public DateTimeOffset LastUpdated => Source?.LastUpdated ?? default;

    public IReadOnlyDictionary<string, int> Cargo => CommanderStateSnapshot.Copy(Source?.Cargo);
    public IReadOnlyDictionary<string, int> RawMaterials => CommanderStateSnapshot.Copy(Source?.Materials.Raw);
    public IReadOnlyDictionary<string, int> ManufacturedMaterials => CommanderStateSnapshot.Copy(Source?.Materials.Manufactured);
    public IReadOnlyDictionary<string, int> EncodedMaterials => CommanderStateSnapshot.Copy(Source?.Materials.Encoded);

    public IReadOnlyCommanderState Snapshot() => CommanderStateSnapshot.Capture(Source);
}

/// <summary>
/// An immutable point-in-time copy of <see cref="CommanderState"/>. Every value is captured when it
/// is built; the collections are <see cref="FrozenDictionary{TKey,TValue}"/> copies, which reject
/// mutation even through a cast to <see cref="IDictionary{TKey,TValue}"/>.
/// </summary>
internal sealed class CommanderStateSnapshot : IReadOnlyCommanderState
{
    private static readonly IReadOnlyDictionary<string, int> Empty =
        FrozenDictionary<string, int>.Empty.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The snapshot of a commander nothing is known about yet.</summary>
    public static CommanderStateSnapshot Unknown { get; } = new();

    public string? Name { get; private init; }
    public long Balance { get; private init; }
    public string? Ship { get; private init; }
    public string? ShipName { get; private init; }
    public string? StarSystem { get; private init; }
    public string? Body { get; private init; }
    public bool Docked { get; private init; }
    public string? StationDisplayName { get; private init; }
    public DateTimeOffset LastUpdated { get; private init; }
    public IReadOnlyDictionary<string, int> Cargo { get; private init; } = Empty;
    public IReadOnlyDictionary<string, int> RawMaterials { get; private init; } = Empty;
    public IReadOnlyDictionary<string, int> ManufacturedMaterials { get; private init; } = Empty;
    public IReadOnlyDictionary<string, int> EncodedMaterials { get; private init; } = Empty;

    public IReadOnlyCommanderState Snapshot() => this;

    /// <summary>Copies <paramref name="state"/>, or returns <see cref="Unknown"/> when it is null.</summary>
    public static CommanderStateSnapshot Capture(CommanderState? state) => state is null ? Unknown : new()
    {
        Name = state.Name,
        Balance = state.Balance,
        Ship = state.Ship,
        ShipName = state.ShipName,
        StarSystem = state.StarSystem,
        Body = state.Body,
        Docked = state.Docked,
        StationDisplayName = state.StationDisplayName,
        LastUpdated = state.LastUpdated,
        Cargo = Copy(state.Cargo),
        RawMaterials = Copy(state.Materials.Raw),
        ManufacturedMaterials = Copy(state.Materials.Manufactured),
        EncodedMaterials = Copy(state.Materials.Encoded),
    };

    /// <summary>
    /// A frozen copy of a live inventory. Enumerating a <see cref="ConcurrentDictionary{TKey,TValue}"/>
    /// is safe while the journal thread writes to it; the copy may miss a concurrent write, never tear.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Copy(ConcurrentDictionary<string, int>? live)
        => live is null || live.IsEmpty ? Empty : live.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
}
