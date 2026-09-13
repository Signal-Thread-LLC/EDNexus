using EDNexus.Core.Twitch;

namespace EDNexus.Tests.Twitch;

/// <summary>Scriptable <see cref="ITwitchApiClient"/> double so auth/refresh logic can be tested without touching the network.</summary>
internal sealed class FakeTwitchApiClient : ITwitchApiClient
{
    public Func<string, string, string, string, TwitchTokenResponse>? OnExchange { get; set; }
    public Func<string, string, TwitchTokenResponse>? OnRefresh { get; set; }
    public Func<string, string, TwitchUser?>? OnGetUser { get; set; }

    public int ExchangeCalls { get; private set; }
    public int RefreshCalls { get; private set; }
    public int RevokeCalls { get; private set; }
    public string? LastRevokedToken { get; private set; }

    public Task<TwitchTokenResponse> ExchangeAuthorizationCodeAsync(string clientId, string code, string codeVerifier, string redirectUri, CancellationToken ct = default)
    {
        ExchangeCalls++;
        var result = OnExchange?.Invoke(clientId, code, codeVerifier, redirectUri)
            ?? throw new InvalidOperationException("OnExchange not configured");
        return Task.FromResult(result);
    }

    public Task<TwitchTokenResponse> RefreshTokenAsync(string clientId, string refreshToken, CancellationToken ct = default)
    {
        RefreshCalls++;
        var result = OnRefresh?.Invoke(clientId, refreshToken) ?? throw new InvalidOperationException("OnRefresh not configured");
        return Task.FromResult(result);
    }

    public Task<TwitchUser?> GetUserAsync(string accessToken, string clientId, CancellationToken ct = default)
    {
        var result = OnGetUser?.Invoke(accessToken, clientId);
        return Task.FromResult(result);
    }

    public Task RevokeTokenAsync(string clientId, string token, CancellationToken ct = default)
    {
        RevokeCalls++;
        LastRevokedToken = token;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Fake loopback listener: returns a pre-scripted callback payload instead of opening a real socket.
/// When a result factory needs to read something the auth service hands the browser launcher first
/// (e.g. the PKCE <c>state</c> embedded in the authorize URL), pass <paramref name="waitFor"/> —
/// typically <see cref="RecordingBrowserLauncher.Opened"/> — so the factory runs only once that has
/// actually happened. A bare <c>Task.Yield()</c> does not guarantee that ordering: without a
/// synchronization context its continuation can be picked up by another thread-pool thread before the
/// caller's subsequent synchronous code (here, <c>IBrowserLauncher.Open</c>) has run.
/// </summary>
internal sealed class FakeCallbackListener : IOAuthCallbackListener
{
    private readonly Func<IReadOnlyDictionary<string, string>>? _result;
    private readonly OperationCanceledException? _cancel;
    private readonly Task? _waitFor;

    public FakeCallbackListener(Func<IReadOnlyDictionary<string, string>> result, Task? waitFor = null)
    {
        _result = result;
        _waitFor = waitFor;
    }
    public FakeCallbackListener(IReadOnlyDictionary<string, string> result) => _result = () => result;
    public FakeCallbackListener(OperationCanceledException toThrow) => _cancel = toThrow;

    public async Task<IReadOnlyDictionary<string, string>> WaitForCallbackAsync(Uri redirectUri, CancellationToken ct)
    {
        if (_waitFor is not null)
            await _waitFor.WaitAsync(ct).ConfigureAwait(false);
        else
            await Task.Yield();
        if (_cancel is not null) throw _cancel;
        return _result!.Invoke();
    }
}

/// <summary>
/// Browser double that, in addition to recording <see cref="LastUrl"/> like <see cref="NoOpBrowserLauncher"/>,
/// exposes <see cref="Opened"/>: a task that completes the instant <see cref="Open"/> is called. Lets a
/// <see cref="FakeCallbackListener"/> wait deterministically for the authorize URL to be recorded instead
/// of racing thread-pool scheduling.
/// </summary>
internal sealed class RecordingBrowserLauncher : IBrowserLauncher
{
    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string? LastUrl { get; private set; }
    public Task Opened => _opened.Task;

    public void Open(string url)
    {
        LastUrl = url;
        _opened.TrySetResult();
    }
}
