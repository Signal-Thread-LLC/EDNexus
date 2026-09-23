using System.Collections.Concurrent;
using EDNexus.Core.Twitch;

namespace EDNexus.Tests.Twitch;

/// <summary>
/// Records what <see cref="TwitchStreamCardService"/> tried to publish, and lets a test decide how
/// the EBS answers.
/// </summary>
internal sealed class FakeStreamStateApiClient : IStreamStateApiClient
{
    private readonly ConcurrentQueue<StreamCardSnapshot> _published = new();

    /// <summary>What the EBS answers with. Defaults to accepting everything.</summary>
    public Func<StreamCardSnapshot, StreamStatePublishResult> Respond { get; set; } =
        _ => StreamStatePublishResult.Ok;

    /// <summary>Signalled after each publish attempt, so a test can await one without polling.</summary>
    public SemaphoreSlim Published { get; } = new(0);

    public IReadOnlyList<StreamCardSnapshot> Snapshots => _published.ToArray();

    public string? LastToken { get; private set; }

    public string? LastEndpoint { get; private set; }

    public Task<StreamStatePublishResult> PublishAsync(
        string updateStateEndpoint, string token, StreamCardSnapshot snapshot, CancellationToken ct = default)
    {
        LastEndpoint = updateStateEndpoint;
        LastToken = token;
        _published.Enqueue(snapshot);
        var result = Respond(snapshot);
        Published.Release();
        return Task.FromResult(result);
    }

    /// <summary>Waits for the next publish attempt, failing the test rather than hanging forever.</summary>
    public async Task<bool> WaitForPublishAsync(TimeSpan? timeout = null) =>
        await Published.WaitAsync(timeout ?? TimeSpan.FromSeconds(5)).ConfigureAwait(false);
}
