using EDNexus.Core.State;
using EDNexus.Core.Twitch;
using Xunit;

namespace EDNexus.Tests.Twitch;

public class TwitchStreamCardServiceTests
{
    private const string Endpoint = "https://ebs.example.com/api/update-state";

    /// <summary>
    /// Short enough that a test never sits on the real five-second floor, long enough that a burst
    /// of changes still lands inside one window on a loaded CI machine.
    /// </summary>
    private static readonly TimeSpan FastInterval = TimeSpan.FromMilliseconds(50);

    private static TwitchStreamCardService Create(
        CommanderState state,
        FakeStreamStateApiClient client,
        Func<string?>? token = null,
        Func<StreamCardVisibility>? visibility = null,
        Func<bool>? isSuppressed = null) =>
        new(
            state,
            StreamCardSources.Empty,
            client,
            static () => Endpoint,
            token ?? (static () => "ebs-token"),
            visibility,
            isSuppressed,
            FastInterval,
            clock: null);

    [Fact]
    public async Task Publishes_what_the_state_already_knows_on_startup()
    {
        var state = new CommanderState { StarSystem = "Nervi", Ship = "Krait Phantom" };
        var client = new FakeStreamStateApiClient();

        using var service = Create(state, client);

        Assert.True(await client.WaitForPublishAsync());
        Assert.Equal("Nervi", client.Snapshots[0].Location!.System);
        Assert.Equal("ebs-token", client.LastToken);
        Assert.Equal(Endpoint, client.LastEndpoint);
    }

    [Fact]
    public async Task Nothing_is_published_until_the_commander_has_logged_in()
    {
        var state = new CommanderState { StarSystem = "Nervi" };
        var client = new FakeStreamStateApiClient();

        using var service = Create(state, client, token: static () => null);

        Assert.False(await client.WaitForPublishAsync(TimeSpan.FromMilliseconds(300)));
        Assert.Empty(client.Snapshots);
    }

    [Fact]
    public async Task Nothing_is_published_while_suppressed()
    {
        var state = new CommanderState { StarSystem = "Nervi" };
        var client = new FakeStreamStateApiClient();

        // Developer mode feeds fabricated journal events through the real bus; they must never reach
        // a live audience.
        using var service = Create(state, client, isSuppressed: static () => true);

        Assert.False(await client.WaitForPublishAsync(TimeSpan.FromMilliseconds(300)));
        Assert.Empty(client.Snapshots);
    }

    [Fact]
    public async Task A_change_that_viewers_cannot_see_does_not_spend_a_publish()
    {
        var state = new CommanderState { StarSystem = "Nervi" };
        var client = new FakeStreamStateApiClient();

        using var service = Create(state, client);
        Assert.True(await client.WaitForPublishAsync());

        // LastUpdated ticks on virtually every journal line but changes nothing on the card.
        for (var i = 0; i < 5; i++)
        {
            state.LastUpdated = DateTimeOffset.UtcNow;
            await Task.Delay(FastInterval);
        }

        Assert.Single(client.Snapshots);
    }

    [Fact]
    public async Task A_real_change_is_published()
    {
        var state = new CommanderState { StarSystem = "Nervi" };
        var client = new FakeStreamStateApiClient();

        using var service = Create(state, client);
        Assert.True(await client.WaitForPublishAsync());

        state.StarSystem = "Colonia";

        Assert.True(await client.WaitForPublishAsync());
        Assert.Equal("Colonia", client.Snapshots[^1].Location!.System);
    }

    [Fact]
    public async Task A_burst_of_changes_is_coalesced_into_the_newest_state()
    {
        var state = new CommanderState { StarSystem = "Nervi" };
        var client = new FakeStreamStateApiClient();

        using var service = Create(state, client);
        Assert.True(await client.WaitForPublishAsync());

        // A single hyperspace jump touches several properties at once; viewers only need the result.
        state.StarSystem = "Colonia";
        state.Body = "Colonia 2 a";
        state.FuelMain = 18.2;
        state.CargoTons = 24;

        Assert.True(await client.WaitForPublishAsync());
        await Task.Delay(FastInterval * 4);

        Assert.True(client.Snapshots.Count < 5, $"Expected a coalesced burst, saw {client.Snapshots.Count} publishes.");
        Assert.Equal("Colonia", client.Snapshots[^1].Location!.System);
    }

    [Fact]
    public async Task A_rejected_token_stops_publishing_and_asks_for_a_new_login()
    {
        var state = new CommanderState { StarSystem = "Nervi" };
        var client = new FakeStreamStateApiClient
        {
            Respond = _ => new StreamStatePublishResult(StreamStatePublishStatus.Unauthorized, "revoked"),
        };

        using var service = Create(state, client);
        var reauth = new TaskCompletionSource();
        service.ReauthRequired += () => reauth.TrySetResult();

        Assert.True(await client.WaitForPublishAsync());
        await reauth.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(service.StoppedForReauth);

        var attempts = client.Snapshots.Count;
        state.StarSystem = "Colonia";
        await Task.Delay(FastInterval * 4);

        // Retrying a revoked grant can only ever earn a rate-limit, so publishing stays stopped.
        Assert.Equal(attempts, client.Snapshots.Count);
    }

    [Fact]
    public async Task Signing_in_again_resumes_publishing_after_a_rejected_token()
    {
        var state = new CommanderState { StarSystem = "Nervi" };
        var client = new FakeStreamStateApiClient
        {
            Respond = _ => new StreamStatePublishResult(StreamStatePublishStatus.Unauthorized, "revoked"),
        };
        var token = "stale-token";

        using var service = Create(state, client, token: () => token);
        Assert.True(await client.WaitForPublishAsync());
        Assert.True(service.StoppedForReauth);

        // The commander logs in again: a different credential is what tells the service the
        // situation has changed, so it never depends on the UI remembering to poke it.
        client.Respond = _ => StreamStatePublishResult.Ok;
        token = "fresh-token";
        state.StarSystem = "Colonia";

        Assert.True(await client.WaitForPublishAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("fresh-token", client.LastToken);
        Assert.False(service.StoppedForReauth);
    }

    [Fact]
    public async Task The_endpoint_is_read_afresh_on_every_publish()
    {
        var state = new CommanderState { StarSystem = "Nervi" };
        var client = new FakeStreamStateApiClient();
        var endpoint = "https://first.example.com/api/update-state";

        using var service = new TwitchStreamCardService(
            state, StreamCardSources.Empty, client, () => endpoint, static () => "ebs-token",
            null, null, FastInterval, clock: null);

        Assert.True(await client.WaitForPublishAsync());
        Assert.Equal("https://first.example.com/api/update-state", client.LastEndpoint);

        // Pointing the app at a different EBS must not need a restart.
        endpoint = "https://second.example.com/api/update-state";
        state.StarSystem = "Colonia";

        Assert.True(await client.WaitForPublishAsync());
        Assert.Equal("https://second.example.com/api/update-state", client.LastEndpoint);
    }

    [Fact]
    public async Task Turning_the_card_off_stops_publishing_without_rebuilding_the_service()
    {
        var state = new CommanderState { StarSystem = "Nervi" };
        var client = new FakeStreamStateApiClient();
        // How EngineHost wires it: "card switched off" reads through as "no token".
        var enabled = true;

        using var service = Create(state, client, token: () => enabled ? "ebs-token" : null);
        Assert.True(await client.WaitForPublishAsync());

        enabled = false;
        var attempts = client.Snapshots.Count;
        state.StarSystem = "Colonia";
        await Task.Delay(FastInterval * 4);

        Assert.Equal(attempts, client.Snapshots.Count);
    }

    [Fact]
    public void Preview_can_map_against_a_visibility_the_commander_has_not_saved_yet()
    {
        var state = new CommanderState { StarSystem = "Nervi", Ship = "Krait Phantom" };
        var client = new FakeStreamStateApiClient();

        using var service = Create(state, client, token: static () => null);

        // What the settings dialog does while the commander is still ticking boxes.
        var preview = service.Preview(StreamCardVisibility.Default with { Ship = false });

        Assert.Null(preview.Ship);
        Assert.NotNull(service.Preview().Ship);
    }

    [Fact]
    public async Task A_failed_publish_is_retried_on_the_next_change()
    {
        var state = new CommanderState { StarSystem = "Nervi" };
        var client = new FakeStreamStateApiClient
        {
            Respond = _ => new StreamStatePublishResult(StreamStatePublishStatus.Failed, "unreachable"),
        };

        using var service = Create(state, client);
        Assert.True(await client.WaitForPublishAsync());

        // The fingerprint is only remembered on success, so the same card is offered again.
        client.Respond = _ => StreamStatePublishResult.Ok;
        state.StarSystem = "Colonia";

        Assert.True(await client.WaitForPublishAsync(TimeSpan.FromSeconds(5)));
        Assert.True(client.Snapshots.Count >= 2);
    }

    [Fact]
    public async Task Hiding_a_section_takes_effect_without_rebuilding_the_service()
    {
        var state = new CommanderState { StarSystem = "Nervi", Ship = "Krait Phantom" };
        var client = new FakeStreamStateApiClient();
        var visibility = StreamCardVisibility.Default;

        using var service = Create(state, client, visibility: () => visibility);
        Assert.True(await client.WaitForPublishAsync());
        Assert.NotNull(client.Snapshots[0].Ship);

        visibility = StreamCardVisibility.Default with { Ship = false };
        state.StarSystem = "Colonia";

        Assert.True(await client.WaitForPublishAsync());
        Assert.Null(client.Snapshots[^1].Ship);
    }

    [Fact]
    public async Task Preview_reports_what_would_be_published_without_sending_it()
    {
        var state = new CommanderState { StarSystem = "Nervi" };
        var client = new FakeStreamStateApiClient();

        using var service = Create(state, client, token: static () => null);

        var preview = service.Preview();

        Assert.Equal("Nervi", preview.Location!.System);
        Assert.Empty(client.Snapshots);
        Assert.False(await client.WaitForPublishAsync(TimeSpan.FromMilliseconds(200)));
    }
}
