using System.Text.Json;
using System.Text.Json.Serialization;

namespace EDNexus.Core.Twitch;

/// <summary>
/// The wire contract between the desktop app and the Twitch extension frontend: one snapshot of
/// everything the commander has chosen to show viewers, as published through
/// <c>POST /api/update-state</c> and relayed to the extension over Extensions PubSub.
/// </summary>
/// <remarks>
/// <para>
/// Twitch caps a PubSub message at 5 KiB, so every section is optional (null sections are omitted
/// entirely — see <see cref="SerializerOptions"/>) and every list is capped by
/// <see cref="StreamCardMapper"/>. Property names are deliberately short for the same reason.
/// </para>
/// <para>
/// This is a *published* contract: the extension frontend in <c>extension/</c> parses exactly these
/// names. Bump <see cref="StreamCardSchema.Version"/> whenever a field changes meaning, and keep the
/// frontend tolerant of missing fields so an older frontend never breaks against a newer app.
/// </para>
/// </remarks>
/// <param name="Version">Schema version, so the frontend can refuse a payload it doesn't understand.</param>
/// <param name="At">When this snapshot was taken (UTC), for the frontend's "stale data" indicator.</param>
/// <param name="Headline">One-line summary for the collapsed flyout handle, e.g. "Docked at Jameson Memorial".</param>
/// <param name="Subline">Secondary line under the headline, e.g. "Flying Anaconda (NX-01)".</param>
/// <param name="Commander">Who the commander is. Null when the broadcaster hides this section.</param>
/// <param name="Ship">Current hull, fuel and hold.</param>
/// <param name="Location">Where the commander is right now.</param>
/// <param name="Carrier">The commander's own fleet carrier, when they have one.</param>
/// <param name="Exobiology">This session's sampling progress and unsold data.</param>
/// <param name="Mining">The last prospected rock.</param>
/// <param name="Missions">Held missions and the stacks they form.</param>
/// <param name="Cargo">The biggest lots in the hold.</param>
/// <param name="CargoMore">
/// How many further lots the hold holds beyond those in <paramref name="Cargo"/>, so the card can
/// say "+7 more" instead of quietly presenting a truncated manifest as the whole hold. 0 when
/// nothing was left out.
/// </param>
public sealed record StreamCardSnapshot(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("at")] DateTimeOffset At,
    [property: JsonPropertyName("headline")] string Headline,
    [property: JsonPropertyName("subline")] string? Subline = null,
    [property: JsonPropertyName("cmdr")] StreamCardCommander? Commander = null,
    [property: JsonPropertyName("ship")] StreamCardShip? Ship = null,
    [property: JsonPropertyName("loc")] StreamCardLocation? Location = null,
    [property: JsonPropertyName("carrier")] StreamCardCarrier? Carrier = null,
    [property: JsonPropertyName("exo")] StreamCardExobiology? Exobiology = null,
    [property: JsonPropertyName("mining")] StreamCardMining? Mining = null,
    [property: JsonPropertyName("missions")] StreamCardMissions? Missions = null,
    [property: JsonPropertyName("cargo")] IReadOnlyList<StreamCardCargoItem>? Cargo = null,
    [property: JsonPropertyName("cargoMore")] int CargoMore = 0)
{
    /// <summary>
    /// The options every snapshot is serialized with. Null sections are dropped rather than sent as
    /// <c>null</c> — both to stay inside Twitch's 5 KiB message cap and so "hidden by the
    /// broadcaster" and "not known yet" look identical to a viewer.
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serializes this snapshot as the <c>state</c> body of a <c>POST /api/update-state</c> call.</summary>
    public JsonElement ToJsonElement() => JsonSerializer.SerializeToElement(this, SerializerOptions);

    /// <summary>
    /// A stable string identifying everything a viewer would see, so two snapshots can be compared for
    /// "did anything actually change".
    /// </summary>
    /// <remarks>
    /// <see cref="At"/> is deliberately excluded: it moves on every journal line, and a publisher that
    /// treated a new timestamp as a change would spend its whole Twitch PubSub quota re-sending an
    /// identical card. The comparison goes through JSON rather than record equality because the record's
    /// generated <c>Equals</c> compares the list-valued sections by reference.
    /// </remarks>
    public string ContentFingerprint() =>
        JsonSerializer.Serialize(this with { At = default }, SerializerOptions);
}

/// <summary>Schema version constants for <see cref="StreamCardSnapshot"/>.</summary>
public static class StreamCardSchema
{
    /// <summary>
    /// Current payload schema version. Bump when a field changes meaning or is removed; the
    /// extension frontend checks this and tells the viewer to expect a newer EDNexus rather than
    /// rendering a payload it does not understand.
    /// </summary>
    public const int Version = 1;
}

/// <param name="Name">The commander's name, exactly as the journal reports it.</param>
/// <param name="Credits">Current balance, or null when the broadcaster hides their balance.</param>
/// <param name="Ranks">Pilot rank standing, one entry per ladder.</param>
public sealed record StreamCardCommander(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("credits")] long? Credits = null,
    [property: JsonPropertyName("ranks")] IReadOnlyList<StreamCardRank>? Ranks = null);

/// <param name="Label">The rank track as the game names it, e.g. "Explorer".</param>
/// <param name="Name">Where the commander stands on it, e.g. "Pathfinder".</param>
/// <param name="Percent">Progress towards the next tier, 0-100.</param>
/// <param name="Elite">True once this ladder is at Elite or beyond, so the frontend can badge it.</param>
public sealed record StreamCardRank(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("pct")] int Percent,
    [property: JsonPropertyName("elite")] bool Elite = false);

/// <param name="Type">Hull type, e.g. "Anaconda".</param>
/// <param name="Name">The name the commander gave the ship.</param>
/// <param name="Ident">The ship's ID plate, e.g. "NX-01".</param>
/// <param name="Fuel">Main tank level (t).</param>
/// <param name="FuelCapacity">Main tank size (t), so the frontend can draw a gauge.</param>
/// <param name="CargoTons">Tonnage currently in the hold.</param>
/// <param name="JumpRange">Maximum jump range (ly) as the game reports it, when a Loadout has been seen.</param>
public sealed record StreamCardShip(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("ident")] string? Ident = null,
    [property: JsonPropertyName("fuel")] double? Fuel = null,
    [property: JsonPropertyName("fuelMax")] double? FuelCapacity = null,
    [property: JsonPropertyName("cargo")] double? CargoTons = null,
    [property: JsonPropertyName("jump")] double? JumpRange = null);

/// <param name="System">Current star system.</param>
/// <param name="Body">Current body, when the commander is near one.</param>
/// <param name="Docked">True while docked.</param>
/// <param name="Station">Where they are docked, named the way a commander would name it.</param>
/// <param name="StationType">The station's kind, e.g. "Coriolis" or "FleetCarrier".</param>
public sealed record StreamCardLocation(
    [property: JsonPropertyName("system")] string? System,
    [property: JsonPropertyName("body")] string? Body = null,
    [property: JsonPropertyName("docked")] bool Docked = false,
    [property: JsonPropertyName("station")] string? Station = null,
    [property: JsonPropertyName("stationType")] string? StationType = null);

/// <param name="Name">The carrier's name.</param>
/// <param name="Callsign">Its callsign, e.g. "K7Q-B3L".</param>
/// <param name="Fuel">Tritium in the reserve (t).</param>
/// <param name="JumpRange">Jump range (ly) at the current load.</param>
/// <param name="PendingSystem">System a booked jump is headed to, when one is scheduled.</param>
/// <param name="DepartsAt">When that booked jump departs.</param>
public sealed record StreamCardCarrier(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("callsign")] string? Callsign = null,
    [property: JsonPropertyName("fuel")] double? Fuel = null,
    [property: JsonPropertyName("jump")] double? JumpRange = null,
    [property: JsonPropertyName("pendingSystem")] string? PendingSystem = null,
    [property: JsonPropertyName("departsAt")] DateTimeOffset? DepartsAt = null);

/// <param name="PendingValue">Credits of analysed-but-unsold data in the sampler.</param>
/// <param name="PendingCount">How many samples are waiting on a Vista Genomics terminal.</param>
/// <param name="SoldValue">Credits already banked this session.</param>
/// <param name="SoldCount">Samples already sold this session.</param>
/// <param name="FirstDiscoveries">First-footfall codex entries logged this session.</param>
/// <param name="ActiveGenus">Genus of the sample run under way.</param>
/// <param name="ActiveSpecies">Species of that run, once the scanner has named it.</param>
/// <param name="SamplesTaken">Samples taken of the active run, out of three.</param>
/// <param name="BodyName">Body being worked.</param>
/// <param name="BodySignals">Biological signal count on that body.</param>
public sealed record StreamCardExobiology(
    [property: JsonPropertyName("pendingValue")] long PendingValue = 0,
    [property: JsonPropertyName("pendingCount")] int PendingCount = 0,
    [property: JsonPropertyName("soldValue")] long SoldValue = 0,
    [property: JsonPropertyName("soldCount")] int SoldCount = 0,
    [property: JsonPropertyName("firsts")] int FirstDiscoveries = 0,
    [property: JsonPropertyName("genus")] string? ActiveGenus = null,
    [property: JsonPropertyName("species")] string? ActiveSpecies = null,
    [property: JsonPropertyName("samples")] int SamplesTaken = 0,
    [property: JsonPropertyName("body")] string? BodyName = null,
    [property: JsonPropertyName("signals")] int BodySignals = 0);

/// <param name="Content">Overall material content of the last rock, e.g. "High".</param>
/// <param name="Motherlode">Deep-core seam in it, when there is one.</param>
/// <param name="Remaining">Percentage of the rock left.</param>
/// <param name="Materials">The richest few materials in it.</param>
/// <param name="Prospected">How many rocks have been prospected this session.</param>
/// <param name="Refined">Tonnes refined this session.</param>
public sealed record StreamCardMining(
    [property: JsonPropertyName("content")] string? Content = null,
    [property: JsonPropertyName("motherlode")] string? Motherlode = null,
    [property: JsonPropertyName("remaining")] double? Remaining = null,
    [property: JsonPropertyName("materials")] IReadOnlyList<StreamCardMiningMaterial>? Materials = null,
    [property: JsonPropertyName("prospected")] int Prospected = 0,
    [property: JsonPropertyName("refined")] int Refined = 0);

/// <param name="Name">Material name as the game shows it.</param>
/// <param name="Percent">Its proportion of the rock.</param>
public sealed record StreamCardMiningMaterial(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("pct")] double Percent);

/// <param name="Active">How many missions are held.</param>
/// <param name="Cap">The game's mission cap, so the frontend can render "12 / 20".</param>
/// <param name="Reward">Total credits the held missions pay out.</param>
/// <param name="Stacks">The biggest massacre stacks being worked.</param>
public sealed record StreamCardMissions(
    [property: JsonPropertyName("active")] int Active = 0,
    [property: JsonPropertyName("cap")] int Cap = 0,
    [property: JsonPropertyName("reward")] long Reward = 0,
    [property: JsonPropertyName("stacks")] IReadOnlyList<StreamCardMissionStack>? Stacks = null);

/// <param name="Target">What is being killed, e.g. "Pirates".</param>
/// <param name="Faction">The faction they belong to.</param>
/// <param name="Count">How many missions are in the stack.</param>
/// <param name="Kills">Kills needed to clear the stack - the largest single mission, not the sum.</param>
/// <param name="Reward">Total payout of the stack.</param>
public sealed record StreamCardMissionStack(
    [property: JsonPropertyName("target")] string? Target,
    [property: JsonPropertyName("faction")] string? Faction,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("kills")] int Kills,
    [property: JsonPropertyName("reward")] long Reward);

/// <param name="Name">Commodity name as the game shows it.</param>
/// <param name="Tons">How many tonnes of it are aboard.</param>
public sealed record StreamCardCargoItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("t")] int Tons);
