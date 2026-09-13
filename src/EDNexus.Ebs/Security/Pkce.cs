using System.Security.Cryptography;
using System.Text;

namespace EDNexus.Ebs.Security;

/// <summary>
/// RFC 7636 (PKCE) verification, mirroring <c>EDNexus.Core.Twitch.PkceUtility</c> on the desktop
/// side. Used at <c>/oauth/token</c> to prove the caller exchanging an authorization code is the
/// same client that started the flow at <c>/oauth/authorize</c>.
/// </summary>
public static class Pkce
{
    /// <summary>Computes the "S256" code challenge for <paramref name="codeVerifier"/>, per RFC 7636 §4.2.</summary>
    public static string ComputeCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Verifies that <paramref name="codeVerifier"/> hashes to the previously-recorded <paramref name="codeChallenge"/>, in constant time.</summary>
    public static bool Verify(string codeVerifier, string codeChallenge)
    {
        if (string.IsNullOrEmpty(codeVerifier) || string.IsNullOrEmpty(codeChallenge))
            return false;

        var computed = Encoding.ASCII.GetBytes(ComputeCodeChallenge(codeVerifier));
        var expected = Encoding.ASCII.GetBytes(codeChallenge);
        return computed.Length == expected.Length && CryptographicOperations.FixedTimeEquals(computed, expected);
    }
}
