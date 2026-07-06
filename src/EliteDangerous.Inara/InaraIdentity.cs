namespace EliteDangerous.Inara;

/// <summary>
/// The per-commander credentials Inara needs to attribute events: the user's personal API key and
/// their in-game identity. Supplied on each send so a single client can serve multiple commanders.
/// </summary>
public sealed class InaraIdentity
{
    /// <summary>The commander's personal Inara API key (from their Inara account settings).</summary>
    public required string ApiKey { get; init; }

    /// <summary>The commander's name, as shown in game.</summary>
    public required string CommanderName { get; init; }

    /// <summary>The commander's Frontier id (the <c>FID</c> from <c>LoadGame</c>), if known.</summary>
    public string? CommanderFrontierID { get; init; }
}
