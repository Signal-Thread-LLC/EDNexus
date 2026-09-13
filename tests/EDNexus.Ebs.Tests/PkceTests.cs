using EDNexus.Ebs.Security;

namespace EDNexus.Ebs.Tests;

public class PkceTests
{
    [Fact]
    public void ComputeCodeChallenge_matches_RFC7636_appendix_B_test_vector()
    {
        // https://datatracker.ietf.org/doc/html/rfc7636#appendix-B
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        Assert.Equal(expectedChallenge, Pkce.ComputeCodeChallenge(verifier));
    }

    [Fact]
    public void Verify_accepts_a_matching_verifier_and_challenge()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var challenge = Pkce.ComputeCodeChallenge(verifier);

        Assert.True(Pkce.Verify(verifier, challenge));
    }

    [Fact]
    public void Verify_rejects_a_mismatched_verifier()
    {
        var challenge = Pkce.ComputeCodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk");

        Assert.False(Pkce.Verify("some-other-verifier-value-1234567890", challenge));
    }

    [Fact]
    public void Verify_rejects_empty_inputs()
    {
        Assert.False(Pkce.Verify("", "challenge"));
        Assert.False(Pkce.Verify("verifier", ""));
    }
}
