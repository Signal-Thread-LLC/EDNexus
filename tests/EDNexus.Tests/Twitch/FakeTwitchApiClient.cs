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
/// The result factory is evaluated lazily (after yielding once) so tests can capture the PKCE
/// <c>state</c> from the authorize URL the auth service hands the browser launcher — which happens
/// after <see cref="WaitForCallbackAsync"/> is called but before it resolves — and echo it back.
/// </summary>
internal sealed class FakeCallbackListener : IOAuthCallbackListener
{
    private readonly Func<IReadOnlyDictionary<string, string>>? _result;
    private readonly OperationCanceledException? _cancel;

    public FakeCallbackListener(Func<IReadOnlyDictionary<string, string>> result) => _result = result;
    public FakeCallbackListener(IReadOnlyDictionary<string, string> result) => _result = () => result;
    public FakeCallbackListener(OperationCanceledException toThrow) => _cancel = toThrow;

    public async Task<IReadOnlyDictionary<string, string>> WaitForCallbackAsync(Uri redirectUri, CancellationToken ct)
    {
        await Task.Yield();
        if (_cancel is not null) throw _cancel;
        return _result!.Invoke();
    }
}
