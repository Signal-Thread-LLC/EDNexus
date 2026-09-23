namespace EDNexus.Core.Twitch;

/// <summary>
/// Which sections of the commander's picture the broadcaster is willing to show viewers.
/// <see cref="StreamCardMapper"/> omits a hidden section from the payload rather than sending it
/// for the frontend to hide — anything that reaches the extension has already left the machine.
/// </summary>
public sealed record StreamCardVisibility(
    bool Commander = true,
    bool Credits = false,
    bool Ship = true,
    bool Location = true,
    bool Carrier = true,
    bool Exobiology = true,
    bool Mining = true,
    bool Missions = true,
    bool Cargo = true)
{
    public static readonly StreamCardVisibility Default = new();

    public static readonly StreamCardVisibility None = new(
        Commander: false, Credits: false, Ship: false, Location: false, Carrier: false,
        Exobiology: false, Mining: false, Missions: false, Cargo: false);
}
