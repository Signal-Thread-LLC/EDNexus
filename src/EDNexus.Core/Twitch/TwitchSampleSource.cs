using System.Text.Json.Nodes;
using EDNexus.Core.Dev;

namespace EDNexus.Core.Twitch;

/// <summary>
/// Developer-mode simulator for the Twitch integration (#40 EBS, #41 OAuth). Neither service exists
/// yet, so this emits synthetic, journal-shaped events under Twitch-specific event names
/// (<c>Twitch*</c>) rather than real Elite Dangerous events — the same mechanism
/// <see cref="JournalSampleSource"/> uses elsewhere, so once the real OAuth/EBS services land they
/// can subscribe to the bus for these names (or a feature service can translate them) without this
/// class changing shape.
/// </summary>
/// <remarks>
/// Simulates, across one call to <see cref="Sample"/>:
/// <list type="bullet">
/// <item>an OAuth redirect landing back at the loopback listener (success or failure),</item>
/// <item>a background token renewal ahead of expiry,</item>
/// <item>a Twitch/EBS API throttle (HTTP 429) while pushing a state update,</item>
/// <item>a token expiring outright and forcing re-authorization, and</item>
/// <item>a burst of rapid game events (system jumps, exobiology scans) so the state-update
/// serializer and its rate-limiter get exercised the way they would mid-stream.</item>
/// </list>
/// </remarks>
public sealed class TwitchSampleSource : JournalSampleSource
{
    public override string CardKey => "twitch";
    public override string DisplayName => "Twitch Integration";

    private static readonly string[] Scopes = { "user:read:email" };

    private static readonly (string Genus, string Species)[] Organisms =
    {
        ("Bacterium", "Bacterium Aurasus"), ("Fungoida", "Fungoida Setisis"),
        ("Tussock", "Tussock Pennata"), ("Osseus", "Osseus Pumice"),
    };

    public override IReadOnlyList<string> Sample(Random rng)
    {
        var lines = new List<string>();
        var broadcaster = SamplePools.Pick(rng, SamplePools.Commanders);

        // 1) OAuth redirect: usually succeeds, but exercise the cancel/deny/timeout path too.
        var authorized = rng.Next(100) >= 15;
        lines.Add(Event("TwitchOAuthRedirect", o =>
        {
            o["Success"] = authorized;
            o["BroadcasterName"] = broadcaster;
            o["Scopes"] = new JsonArray(Scopes.Select(s => (JsonNode)s!).ToArray());
            if (!authorized)
                o["ErrorReason"] = Pick(rng, new[] { "access_denied", "timeout", "state_mismatch" });
        }));

        if (!authorized)
            return lines;

        var expiresIn = rng.Next(1800, 14_400); // 30 min – 4 hr, like a real Twitch user token.
        lines.Add(Event("TwitchTokenRenewed", o =>
        {
            o["BroadcasterName"] = broadcaster;
            o["TokenType"] = "bearer";
            o["ExpiresInSeconds"] = expiresIn;
        }));

        // 2) A burst of rapid game events, as if mid-stream, so the state-update serializer and its
        // rate-limiter (only push to the EBS every N seconds / on meaningful change) get exercised.
        var burstSize = rng.Next(3, 8);
        for (var i = 0; i < burstSize; i++)
        {
            var system = Pick(rng, SamplePools.Systems);
            if (rng.Next(2) == 0)
            {
                lines.Add(Event("FSDJump", o =>
                {
                    o["StarSystem"] = system;
                    o["Body"] = system + Pick(rng, SamplePools.BodySuffixes);
                }));
            }
            else
            {
                var (genus, species) = Pick(rng, Organisms);
                lines.Add(Event("ScanOrganic", o =>
                {
                    o["ScanType"] = Pick(rng, new[] { "Log", "Sample", "Analyse" });
                    o["Genus"] = "$Codex_Ent_" + genus + "_Genus;";
                    o["Genus_Localised"] = genus;
                    o["Species"] = "$Codex_Ent_" + genus + "_01_Name;";
                    o["Species_Localised"] = species;
                    o["SystemAddress"] = (long)rng.Next(1_000_000, int.MaxValue) * 1000;
                }));
            }
        }

        // 3) Roughly a third of the time, the EBS/Twitch Helix API throttles a state push (HTTP 429).
        if (rng.Next(100) < 35)
        {
            lines.Add(Event("TwitchApiThrottled", o =>
            {
                o["Endpoint"] = "/api/update-state";
                o["StatusCode"] = 429;
                o["RetryAfterSeconds"] = rng.Next(5, 60);
            }));
        }

        // 4) Occasionally the token expires outright (revoked, or the background renewal missed its
        // window) and the commander needs to re-authorize.
        if (rng.Next(100) < 20)
        {
            lines.Add(Event("TwitchTokenExpired", o =>
            {
                o["BroadcasterName"] = broadcaster;
                o["Reason"] = Pick(rng, new[] { "expired", "revoked" });
            }));
        }

        return lines;
    }
}
