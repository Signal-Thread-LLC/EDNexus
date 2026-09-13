using System.Security.Cryptography;
using System.Text;

namespace EDNexus.Core.Twitch;

/// <summary>
/// RFC 7636 (PKCE) helpers used by <see cref="TwitchAuthService"/>: a random code verifier, its
/// SHA-256 "S256" challenge, and an unrelated random CSRF state token for the authorization request.
/// Pure/static and side-effect free, so it needs no mocking to test.
/// </summary>
public static class PkceUtility
{
    /// <summary>Number of random bytes backing the verifier — 32 bytes base64url-encodes to 43 characters (the RFC minimum).</summary>
    private const int VerifierByteLength = 32;

    /// <summary>Number of random bytes backing the CSRF state token.</summary>
    private const int StateByteLength = 24;

    /// <summary>
    /// Generates a new PKCE code verifier: a cryptographically random string using only the RFC 7636
    /// "unreserved" character set (implied by base64url encoding), 43 characters long.
    /// </summary>
    public static string GenerateCodeVerifier() => Base64UrlEncode(RandomNumberGenerator.GetBytes(VerifierByteLength));

    /// <summary>
    /// Computes the "S256" code challenge for <paramref name="codeVerifier"/>: the base64url-encoded
    /// SHA-256 hash of the verifier's ASCII bytes, per RFC 7636 §4.2.
    /// </summary>
    public static string ComputeCodeChallenge(string codeVerifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    /// <summary>Generates a random opaque token used to correlate the authorization request/response and guard against CSRF.</summary>
    public static string GenerateState() => Base64UrlEncode(RandomNumberGenerator.GetBytes(StateByteLength));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
