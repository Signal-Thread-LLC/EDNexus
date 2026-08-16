using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using EDNexus.Core.Colonisation;
using EDNexus.Tests.Reporting;   // reuse the shared RecordingHandler test double
using EliteDangerous.RavenColonial;
using Xunit;

namespace EDNexus.Tests.Colonisation;

public class RavenColonialTests
{
    /// <summary>
    /// Shaped like a real ProjectView. Note the numbers: the published schema declares them as
    /// integer-or-string, and the live service does return some as strings.
    /// </summary>
    private const string ProjectJson = """
    {
      "buildId": "3684c1a4-46b2-467f-9a1e-0eee8c876056",
      "buildName": "Hutton Annex",
      "buildType": "prometheus",
      "systemName": "Sol",
      "systemAddress": 10477373803,
      "marketId": "123456",
      "sumNeed": 5170,
      "maxNeed": 21620,
      "complete": false,
      "architectName": "Cloudas",
      "commodities": { "cmmcomposite": 4450, "aluminium": "520", "copper": 200 },
      "commanders": { "Cloudas": ["cmmcomposite"], "Jameson": [] },
      "ready": []
    }
    """;

    private static readonly RavenColonialClientOptions Options = new()
    {
        SoftwareName = "EDNexus.Tests",
        SoftwareVersion = "1.0.0",
    };

    private static RavenColonialClient NewClient(RecordingHandler handler)
        => new(Options, new HttpClient(handler));

    // --- Client ---

    [Fact]
    public async Task A_project_parses_including_numbers_the_service_sends_as_strings()
    {
        var project = (await NewClient(new RecordingHandler(body: ProjectJson))
            .GetProjectAsync("3684c1a4-46b2-467f-9a1e-0eee8c876056")).Value!;

        Assert.Equal("Hutton Annex", project.BuildName);
        Assert.Equal("Sol", project.SystemName);
        Assert.Equal(10477373803L, project.SystemAddress);
        Assert.Equal(123456L, project.MarketId);          // arrived as a string
        Assert.Equal(520, project.Remaining["aluminium"]); // arrived as a string
        Assert.Equal(5170, project.SumRemaining);
        Assert.Equal(21620, project.MaxNeed);
        Assert.False(project.Complete);
        Assert.Equal("Cloudas", project.Architect);
        Assert.Equal(new[] { "Cloudas", "Jameson" }, project.Contributors);
    }

    [Fact]
    public async Task A_depot_nobody_tracks_is_an_ordinary_empty_answer_not_a_failure()
    {
        // Most builds are solo and never registered, so 404 here is normal, not a fault.
        var result = await NewClient(new RecordingHandler(HttpStatusCode.NotFound, "")).GetProjectForDepotAsync(1, 2);

        Assert.True(result.IsOk);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task A_server_error_is_a_failure_with_the_status_in_it()
    {
        var result = await NewClient(new RecordingHandler(HttpStatusCode.InternalServerError, "boom"))
            .GetProjectAsync("abc");

        Assert.False(result.IsOk);
        Assert.Contains("500", result.Error);
    }

    [Fact]
    public async Task Junk_in_place_of_a_project_is_a_failure_not_an_exception()
    {
        var result = await NewClient(new RecordingHandler(body: "<html>not json</html>")).GetProjectAsync("abc");

        Assert.False(result.IsOk);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task An_empty_build_id_never_reaches_the_network()
    {
        var handler = new RecordingHandler(body: ProjectJson);

        var result = await NewClient(handler).GetProjectAsync("   ");

        Assert.True(result.IsOk);
        Assert.Null(result.Value);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task A_systems_projects_list_parses_into_summaries()
    {
        const string json = """
        [
          { "buildId": "a", "buildName": "First", "buildType": "prometheus", "systemName": "Sol",
            "marketId": 111, "systemAddress": 10477373803, "complete": false, "architectName": "Cloudas" },
          { "buildId": "b", "buildName": "Second", "buildType": "vulcan", "systemName": "Sol",
            "marketId": 222, "systemAddress": 10477373803, "complete": true }
        ]
        """;

        var projects = (await NewClient(new RecordingHandler(body: json)).GetSystemProjectsAsync("Sol")).Value!;

        Assert.Equal(new[] { "a", "b" }, projects.Select(p => p.BuildId));
        Assert.Equal(111L, projects[0].MarketId);
        Assert.True(projects[1].Complete);
        Assert.Null(projects[1].Architect);
    }

    [Fact]
    public async Task An_entry_with_no_build_id_is_dropped_from_a_system_listing()
    {
        const string json = """[ { "buildName": "Orphan" }, { "buildId": "b", "buildName": "Real" } ]""";

        var projects = (await NewClient(new RecordingHandler(body: json)).GetSystemProjectsAsync("Sol")).Value!;

        Assert.Equal("b", Assert.Single(projects).BuildId);
    }

    // --- Core adapter ---

    private static RavenColonialProjectLookup NewLookup(RecordingHandler handler)
        => new(NewClient(handler));

    /// <summary>
    /// The adapter makes two calls — list the system, then fetch the matching project — so the
    /// handler answers the listing first and the project second.
    /// </summary>
    private static RecordingHandler DepotHandler(string projectJson, long marketId = 123456)
        => new(n => (HttpStatusCode.OK, n == 1
            ? $$"""[ { "buildId": "3684c1a4-46b2-467f-9a1e-0eee8c876056", "marketId": {{marketId}}, "systemName": "Sol" } ]"""
            : projectJson));

    [Fact]
    public async Task The_adapter_maps_the_shared_totals_for_a_depot()
    {
        var shared = await NewLookup(DepotHandler(ProjectJson)).GetForDepotAsync("Sol", 123456);

        Assert.NotNull(shared);
        Assert.Equal("Hutton Annex", shared!.BuildName);
        Assert.Equal(5170, shared.SumRemaining);
        Assert.Equal(4450, shared.Remaining["cmmcomposite"]);
        Assert.Equal("Raven Colonial", shared.SourceName);
        Assert.Equal(new[] { "Cloudas", "Jameson" }, shared.Contributors);
    }

    [Fact]
    public async Task Commodity_keys_are_normalised_so_they_join_the_depots_own_symbols()
    {
        // Raven can key a commodity in any journal form; the depot lines are canonicalised, so these
        // have to fold onto the same key to be joinable at all — and fold together when they collide.
        const string json = """
        {
          "buildId": "x", "buildName": "n", "buildType": "t", "systemName": "Sol",
          "commodities": { "$CMMComposite_name;": 100, "cmmcomposite": 50 }
        }
        """;

        var shared = await NewLookup(DepotHandler(json)).GetForDepotAsync("Sol", 123456);

        Assert.Equal(150, shared!.Remaining["cmmcomposite"]);
    }

    [Fact]
    public async Task Sum_remaining_falls_back_to_the_commodity_lines_when_the_service_omits_it()
    {
        const string json = """
        {
          "buildId": "x", "buildName": "n", "buildType": "t", "systemName": "Sol",
          "commodities": { "aluminium": 10, "copper": 5 }
        }
        """;

        var shared = await NewLookup(DepotHandler(json)).GetForDepotAsync("Sol", 123456);

        Assert.Equal(15, shared!.SumRemaining);
    }

    [Theory]
    [InlineData("", 123456)]
    [InlineData("   ", 123456)]
    [InlineData("Sol", 0)]
    public async Task An_incomplete_depot_identity_never_reaches_the_network(string systemName, long marketId)
    {
        // Before the game has reported both the system and the depot there is nothing to match on.
        var handler = new RecordingHandler(body: ProjectJson);

        Assert.Null(await NewLookup(handler).GetForDepotAsync(systemName, marketId));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task A_depot_the_system_listing_does_not_mention_is_left_unmatched()
    {
        // The commander is at a depot nobody registered, in a system that has other builds.
        var handler = DepotHandler(ProjectJson, marketId: 999);

        Assert.Null(await NewLookup(handler).GetForDepotAsync("Sol", 123456));
        Assert.Equal(1, handler.CallCount);   // stopped after the listing; no project fetch
    }

    [Fact]
    public async Task An_unreachable_service_leaves_the_local_depot_alone()
    {
        // The local depot snapshot is authoritative for the commander's own deliveries; a shared
        // lookup that fails must degrade to "no shared data", never to an error on the card.
        var lookup = NewLookup(new RecordingHandler(HttpStatusCode.ServiceUnavailable, "down"));

        Assert.Null(await lookup.GetForDepotAsync("Sol", 123456));
    }
}
