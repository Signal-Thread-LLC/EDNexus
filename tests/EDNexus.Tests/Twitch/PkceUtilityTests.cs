using EDNexus.Core.Twitch;
using Xunit;

namespace EDNexus.Tests.Twitch;

public class PkceUtilityTests
{
    [Fact]
    public void ComputeCodeChallenge_matches_RFC7636_appendix_B_test_vector()
    {
        // https://datatracker.ietf.org/doc/html/rfc7636#appendix-B
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        Assert.Equal(expectedChallenge, PkceUtility.ComputeCodeChallenge(verifier));
    }

    [Fact]
    public void GenerateCodeVerifier_is_url_safe_and_within_RFC_length_bounds()
    {
        var verifier = PkceUtility.GenerateCodeVerifier();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", verifier);
    }

    [Fact]
    public void GenerateCodeVerifier_returns_distinct_values_each_call()
    {
        var a = PkceUtility.GenerateCodeVerifier();
        var b = PkceUtility.GenerateCodeVerifier();

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void GenerateState_is_url_safe_and_nonempty()
    {
        var state = PkceUtility.GenerateState();

        Assert.NotEmpty(state);
        Assert.Matches("^[A-Za-z0-9_-]+$", state);
    }

    [Fact]
    public void GenerateState_returns_distinct_values_each_call()
    {
        var a = PkceUtility.GenerateState();
        var b = PkceUtility.GenerateState();

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ComputeCodeChallenge_is_deterministic_for_the_same_verifier()
    {
        var verifier = PkceUtility.GenerateCodeVerifier();

        Assert.Equal(PkceUtility.ComputeCodeChallenge(verifier), PkceUtility.ComputeCodeChallenge(verifier));
    }
}
