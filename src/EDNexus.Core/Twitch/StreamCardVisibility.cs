namespace EDNexus.Core.Twitch;

/// <summary>
/// Which sections of the commander's picture the broadcaster is willing to put in front of viewers.
/// </summary>
/// <remarks>
/// Everything published here lands on a public stream, so this is opt-in per section rather than a
/// single on/off switch: plenty of commanders will happily show where they are and what they are
/// flying while keeping their balance to themselves. <see cref="StreamCardMapper"/> omits a hidden
/// section from the payload entirely — it is never sent and then hidden client-side, because
/// anything that reaches the extension frontend has already left the broadcaster's machine.
/// </remarks>
/// <param name="Commander">Commander name and rank standing.</param>
/// <param name="Credits">The credit balance. Off by default — it is the most commonly withheld field.</param>
/// <param name="Ship">Hull, fuel and hold.</param>
/// <param name="Location">System, body, and docked station.</param>
/// <param name="Carrier">The commander's own fleet carrier and any booked jump.</param>
/// <param name="Exobiology">Sampling progress and unsold Vista Genomics data.</param>
/// <param name="Mining">The last prospected rock and this session's yield.</param>
/// <param name="Missions">Held missions and massacre stacks.</param>
/// <param name="Cargo">The manifest of the hold.</param>
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
    /// <summary>The shipped default: everything except the credit balance.</summary>
    public static readonly StreamCardVisibility Default = new();

    /// <summary>Nothing but the headline — the conservative choice for a first run.</summary>
    public static readonly StreamCardVisibility None = new(
        Commander: false, Credits: false, Ship: false, Location: false, Carrier: false,
        Exobiology: false, Mining: false, Missions: false, Cargo: false);
}
