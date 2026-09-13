namespace EDNexus.Ebs.Security;

/// <summary>
/// Signs and verifies the compact HS256 JWTs used by the Twitch Extensions platform: both the
/// tokens Twitch issues to the extension frontend (which the EBS must verify) and the tokens the
/// EBS itself mints to authenticate its own calls to the Helix PubSub API (role "external").
/// </summary>
public interface ITwitchExtensionJwtService
{
    /// <summary>
    /// Validates the signature and standard claims (<c>exp</c>, <c>channel_id</c>, <c>role</c>) of a
    /// JWT issued by Twitch for the extension, using the base64-encoded Extension Secret.
    /// </summary>
    /// <param name="token">The compact JWT string, without the "Bearer " prefix.</param>
    TwitchJwtValidationResult Validate(string token);

    /// <summary>
    /// Mints a short-lived "external" role JWT, signed with the Extension Secret, suitable for
    /// authorizing a call to <c>POST https://api.twitch.tv/helix/extensions/pubsub</c> on behalf of
    /// the given broadcaster channel.
    /// </summary>
    /// <param name="channelId">The broadcaster channel id the message will be sent to.</param>
    string CreateExternalServiceToken(string channelId);
}
