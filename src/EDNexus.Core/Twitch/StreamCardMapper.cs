using EDNexus.Core.Missions;
using EDNexus.Core.State;

namespace EDNexus.Core.Twitch;

/// <summary>
/// Pure translation from the live engine picture to the <see cref="StreamCardSnapshot"/> viewers
/// see. Kept free of any I/O or Twitch client so it can be unit tested by constructing a
/// <see cref="CommanderState"/> and asserting on the mapped fields — the same shape as
/// <c>DiscordPresenceMapper</c>.
/// </summary>
/// <remarks>
/// Two rules drive every decision here. First, a hidden section is never populated, so nothing the
/// broadcaster withheld can reach the extension at all. Second, Twitch caps a PubSub message at
/// 5 KiB, so every list is capped: the card is a glanceable summary for a viewer, not a data dump.
/// </remarks>
public static class StreamCardMapper
{
    /// <summary>Most cargo lots listed, biggest first. The rest are summarised as "+N more" by the frontend.</summary>
    public const int MaxCargoItems = 8;

    /// <summary>Most materials listed from the last prospected rock, richest first.</summary>
    public const int MaxMiningMaterials = 4;

    /// <summary>Most massacre stacks listed, biggest payout first.</summary>
    public const int MaxMissionStacks = 3;

    /// <param name="state">The live commander picture. Read-only.</param>
    /// <param name="sources">Feature trackers to draw the richer sections from. Defaults to none.</param>
    /// <param name="visibility">What the broadcaster has agreed to show. Defaults to <see cref="StreamCardVisibility.Default"/>.</param>
    /// <param name="now">Timestamp to record on the snapshot. Defaults to now.</param>
    public static StreamCardSnapshot Map(
        CommanderState state,
        StreamCardSources? sources = null,
        StreamCardVisibility? visibility = null,
        DateTimeOffset? now = null)
    {
        sources ??= StreamCardSources.Empty;
        visibility ??= StreamCardVisibility.Default;

        return new StreamCardSnapshot(
            Version: StreamCardSchema.Version,
            At: now ?? DateTimeOffset.UtcNow,
            Headline: BuildHeadline(state, visibility),
            Subline: BuildSubline(state, visibility),
            Commander: visibility.Commander ? MapCommander(state, sources, visibility) : null,
            Ship: visibility.Ship ? MapShip(state) : null,
            Location: visibility.Location ? MapLocation(state) : null,
            Carrier: visibility.Carrier ? MapCarrier(state) : null,
            Exobiology: visibility.Exobiology ? MapExobiology(sources) : null,
            Mining: visibility.Mining ? MapMining(sources) : null,
            Missions: visibility.Missions ? MapMissions(sources) : null,
            Cargo: visibility.Cargo ? MapCargo(state) : null);
    }

    /// <summary>
    /// The single line shown on the collapsed flyout handle — the one thing a viewer sees without
    /// opening the card, so it answers "where is this commander right now".
    /// </summary>
    private static string BuildHeadline(CommanderState state, StreamCardVisibility visibility)
    {
        if (!visibility.Location) return "Elite Dangerous";

        if (state.Docked && !string.IsNullOrWhiteSpace(state.StationDisplayName))
            return $"Docked at {state.StationDisplayName}";

        if (string.IsNullOrWhiteSpace(state.StarSystem)) return "In the black";

        return !string.IsNullOrWhiteSpace(state.Body)
            && !string.Equals(state.Body, state.StarSystem, StringComparison.OrdinalIgnoreCase)
                ? $"{state.StarSystem} / {state.Body}"
                : state.StarSystem!;
    }

    private static string? BuildSubline(CommanderState state, StreamCardVisibility visibility)
    {
        if (!visibility.Ship || string.IsNullOrWhiteSpace(state.Ship)) return null;

        return string.IsNullOrWhiteSpace(state.ShipIdent)
            ? $"Flying {state.Ship}"
            : $"Flying {state.Ship} ({state.ShipIdent})";
    }

    private static StreamCardCommander MapCommander(
        CommanderState state, StreamCardSources sources, StreamCardVisibility visibility)
    {
        var ranks = sources.Ranks is { HasData: true } tracker
            ? tracker.All
                .Select(r => new StreamCardRank(r.Label, r.Name, r.Percent, r.IsElite))
                .ToArray()
            : null;

        return new StreamCardCommander(
            Name: state.Name,
            Credits: visibility.Credits ? state.Balance : null,
            Ranks: ranks is { Length: > 0 } ? ranks : null);
    }

    private static StreamCardShip MapShip(CommanderState state) =>
        new(
            Type: state.Ship,
            Name: state.ShipName,
            Ident: state.ShipIdent,
            Fuel: Round(state.FuelMain, 2),
            FuelCapacity: Positive(state.FuelCapacity),
            CargoTons: state.CargoTons > 0 ? state.CargoTons : null,
            JumpRange: CurrentJumpRange(state));

    /// <summary>
    /// Jump range at the ship's present load — hull plus the fuel and cargo actually aboard — rather
    /// than the headline unladen figure, because that is the number a viewer watching the commander
    /// plot a route would recognise. Null until a <c>Loadout</c> has been seen.
    /// </summary>
    private static double? CurrentJumpRange(CommanderState state)
    {
        if (state.Fsd is not { } fsd || fsd.BaseMass <= 0) return null;

        var mass = fsd.BaseMass + state.FuelMain + state.CargoTons;
        var range = fsd.JumpRangeAt(mass);
        return range > 0 ? Math.Round(range, 2) : null;
    }

    private static StreamCardLocation MapLocation(CommanderState state) =>
        new(
            System: state.StarSystem,
            Body: state.Body,
            Docked: state.Docked,
            Station: state.Docked ? state.StationDisplayName : null,
            StationType: state.Docked ? state.StationType : null);

    private static StreamCardCarrier? MapCarrier(CommanderState state)
    {
        // Nothing to show until a CarrierStats event has identified one as the commander's own.
        if (string.IsNullOrWhiteSpace(state.CarrierName) && string.IsNullOrWhiteSpace(state.CarrierCallsign))
            return null;

        return new StreamCardCarrier(
            Name: state.CarrierName,
            Callsign: state.CarrierCallsign,
            Fuel: Positive(state.CarrierFuel),
            JumpRange: Positive(state.CarrierJumpRange),
            PendingSystem: state.CarrierPendingSystem,
            DepartsAt: state.CarrierPendingDeparture);
    }

    private static StreamCardExobiology? MapExobiology(StreamCardSources sources)
    {
        if (sources.Exobiology is not { } exo) return null;

        var session = exo.Session;
        var active = exo.ActiveScan;
        var body = exo.CurrentBody;

        var hasAnything = session.PendingValue > 0 || session.SoldValue > 0 || session.SoldCount > 0
            || active is not null || body is not null || exo.NewDiscoveries > 0;
        if (!hasAnything) return null;

        return new StreamCardExobiology(
            PendingValue: session.PendingValue,
            PendingCount: session.Pending.Count,
            SoldValue: session.SoldValue,
            SoldCount: session.SoldCount,
            FirstDiscoveries: exo.NewDiscoveries,
            ActiveGenus: active?.GenusName,
            ActiveSpecies: active?.SpeciesName,
            SamplesTaken: active?.Samples ?? 0,
            BodyName: body?.BodyName ?? active?.BodyName,
            BodySignals: body?.SignalCount ?? 0);
    }

    private static StreamCardMining? MapMining(StreamCardSources sources)
    {
        if (sources.Mining is not { } mining) return null;

        var latest = mining.Latest;
        if (latest is null && mining.History.Count == 0 && mining.Refined.Count == 0) return null;

        var materials = latest?.Materials
            .OrderByDescending(m => m.Proportion)
            .Take(MaxMiningMaterials)
            .Select(m => new StreamCardMiningMaterial(m.Name, Math.Round(m.Proportion, 1)))
            .ToArray();

        return new StreamCardMining(
            Content: string.IsNullOrWhiteSpace(latest?.Content) ? null : latest.Content,
            Motherlode: latest?.MotherlodeName,
            Remaining: latest is null ? null : Math.Round(latest.Remaining, 1),
            Materials: materials is { Length: > 0 } ? materials : null,
            Prospected: mining.History.Count,
            Refined: mining.Refined.Count);
    }

    private static StreamCardMissions? MapMissions(StreamCardSources sources)
    {
        if (sources.Missions is not { } missions) return null;

        var active = missions.Active;
        if (active.Count == 0) return null;

        var stacks = missions.Stacks
            .OrderByDescending(s => s.Missions.Sum(m => m.Reward))
            .Take(MaxMissionStacks)
            .Select(s => new StreamCardMissionStack(
                Target: s.TargetType,
                Faction: s.TargetFaction,
                Count: s.Missions.Count,
                Kills: s.KillsToClear,
                Reward: s.Missions.Sum(m => m.Reward)))
            .ToArray();

        return new StreamCardMissions(
            Active: active.Count,
            Cap: MissionTracker.MissionCap,
            Reward: active.Sum(m => m.Reward),
            Stacks: stacks is { Length: > 0 } ? stacks : null);
    }

    private static IReadOnlyList<StreamCardCargoItem>? MapCargo(CommanderState state)
    {
        if (state.Cargo.IsEmpty) return null;

        var items = state.Cargo
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(MaxCargoItems)
            .Select(kv => new StreamCardCargoItem(kv.Key, kv.Value))
            .ToArray();

        return items.Length > 0 ? items : null;
    }

    private static double? Positive(double value) => value > 0 ? value : null;

    private static double? Round(double value, int digits) => value > 0 ? Math.Round(value, digits) : null;
}
