namespace EDNexus.Ebs.Security;

/// <summary>
/// The subset of claims Twitch embeds in an Extension JWT that the EBS cares about.
/// See https://dev.twitch.tv/docs/extensions/reference/#jwt-schema for the full schema.
/// </summary>
/// <param name="ChannelId">The broadcaster's channel/user id the extension is active on.</param>
/// <param name="UserId">The identified viewer/broadcaster user id, if identity was shared.</param>
/// <param name="OpaqueUserId">A pseudonymous id for the user, always present.</param>
/// <param name="Role">One of "broadcaster", "moderator", "viewer", or "external".</param>
/// <param name="ExpiresAtUnixSeconds">The <c>exp</c> claim, seconds since the Unix epoch.</param>
public sealed record TwitchExtensionClaims(
    string? ChannelId,
    string? UserId,
    string? OpaqueUserId,
    string? Role,
    long ExpiresAtUnixSeconds)
{
    /// <summary>True when the token's role identifies the channel's broadcaster.</summary>
    public bool IsBroadcaster => string.Equals(Role, "broadcaster", StringComparison.OrdinalIgnoreCase);
}
