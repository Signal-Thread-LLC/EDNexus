using System.Security.Cryptography;

namespace EDNexus.Ebs.Security;

/// <summary>
/// Generates cryptographically random, URL-safe opaque tokens/ids: the long-lived per-broadcaster
/// credential handed back to the desktop client, the pending-session id used between
/// <c>/oauth/authorize</c> and <c>/oauth/callback</c>, and the one-time authorization code exchanged
/// at <c>/oauth/token</c>.
/// </summary>
public static class OpaqueToken
{
    /// <summary>Byte length used for the long-lived broadcaster token — generous, since it lives for a long time.</summary>
    public const int TokenByteLength = 32;

    /// <summary>Byte length used for short-lived session ids and authorization codes.</summary>
    public const int ShortLivedByteLength = 24;

    /// <summary>Generates a new random, base64url-encoded token of <paramref name="byteLength"/> random bytes.</summary>
    public static string Generate(int byteLength = TokenByteLength) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteLength)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
