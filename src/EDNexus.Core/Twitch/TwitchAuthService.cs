using EDNexus.Core.Settings;

namespace EDNexus.Core.Twitch;

/// <summary>
/// A snapshot of the current Twitch session, safe to read from a UI thread without touching
/// <see cref="AppSettings"/> directly.
/// </summary>
public sealed record TwitchSessionState(bool LoggedIn, string? Username, string? UserId, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Owns the Twitch OAuth 2.0 Authorization Code + PKCE flow: builds the authorization URL, opens the
/// commander's browser, runs a temporary loopback listener for the redirect, exchanges the code for
/// tokens, resolves the broadcaster's identity, and persists everything via <see cref="SettingsStore"/>
/// (the same convention as the Inara API key). Also owns background-refresh: <see cref="EnsureValidTokenAsync"/>
/// silently renews the access token once it is near expiry, and clears the session if the refresh
/// token itself has been revoked.
/// </summary>
/// <remarks>
/// Standalone by design — this does not depend on the journal bus, <c>EngineHost</c>, or any UI.
/// Callers that need a bearer token (e.g. an EBS overlay bridge) should call
/// <see cref="EnsureValidTokenAsync"/> before <see cref="AccessToken"/> to guarantee freshness.
/// </remarks>
public sealed class TwitchAuthService
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly TwitchOAuthOptions _options;
    private readonly ITwitchApiClient _api;
    private readonly IBrowserLauncher _browser;
    private readonly IOAuthCallbackListener _listener;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public TwitchAuthService(
        AppSettings settings,
        SettingsStore store,
        TwitchOAuthOptions options,
        ITwitchApiClient? api = null,
        IBrowserLauncher? browser = null,
        IOAuthCallbackListener? listener = null,
        Func<DateTimeOffset>? clock = null)
    {
        _settings = settings;
        _store = store;
        _options = options;
        _api = api ?? new TwitchApiClient(options);
        _browser = browser ?? new SystemBrowserLauncher();
        _listener = listener ?? new LoopbackOAuthCallbackListener();
        _now = clock ?? (() => DateTimeOffset.UtcNow);
    }

    private TwitchSettings Twitch => _settings.Twitch;

    /// <summary>True once a broadcaster identity and access token are on file (may still be expired — see <see cref="EnsureValidTokenAsync"/>).</summary>
    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(Twitch.AccessToken) && !string.IsNullOrWhiteSpace(Twitch.UserId);

    /// <summary>The current access token, if logged in. Callers wanting a guaranteed-fresh token should call <see cref="EnsureValidTokenAsync"/> first.</summary>
    public string? AccessToken => IsLoggedIn ? Twitch.AccessToken : null;

    /// <summary>A UI/consumer-friendly snapshot of the current session.</summary>
    public TwitchSessionState State => new(IsLoggedIn, Twitch.Username, Twitch.UserId, Twitch.ExpiresAtUtc);

    /// <summary>
    /// Runs the full authorization-code + PKCE flow: opens the browser to Twitch's consent page,
    /// waits on a temporary loopback listener for the redirect, exchanges the code for tokens, looks
    /// up the broadcaster's Twitch identity, and persists the result. Never throws for flow failures
    /// (cancellation, denial, timeout, transport errors) — those come back as a non-success
    /// <see cref="TwitchAuthResult"/>.
    /// </summary>
    public async Task<TwitchAuthResult> LoginAsync(CancellationToken ct = default)
    {
        var verifier = PkceUtility.GenerateCodeVerifier();
        var challenge = PkceUtility.ComputeCodeChallenge(verifier);
        var state = PkceUtility.GenerateState();
        var redirectUri = new Uri(_options.RedirectUri);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.LoginTimeout);

        Task<IReadOnlyDictionary<string, string>> waitTask;
        try
        {
            waitTask = _listener.WaitForCallbackAsync(redirectUri, timeoutCts.Token);
        }
        catch (Exception ex)
        {
            return TwitchAuthResult.Failed(TwitchAuthStatus.Error, $"Could not start the local OAuth callback listener: {ex.Message}");
        }

        try
        {
            _browser.Open(BuildAuthorizeUrl(challenge, state));
        }
        catch (Exception ex)
        {
            return TwitchAuthResult.Failed(TwitchAuthStatus.Error, $"Could not open the default browser: {ex.Message}");
        }

        IReadOnlyDictionary<string, string> callback;
        try
        {
            callback = await waitTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ct.IsCancellationRequested
                ? TwitchAuthResult.Failed(TwitchAuthStatus.Cancelled)
                : TwitchAuthResult.Failed(TwitchAuthStatus.Timeout);
        }
        catch (Exception ex)
        {
            return TwitchAuthResult.Failed(TwitchAuthStatus.Error, ex.Message);
        }

        if (callback.TryGetValue("error", out var error))
        {
            var denied = string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase);
            var message = callback.GetValueOrDefault("error_description", error);
            return TwitchAuthResult.Failed(denied ? TwitchAuthStatus.Denied : TwitchAuthStatus.Error, message);
        }

        if (!callback.TryGetValue("state", out var returnedState) || !string.Equals(returnedState, state, StringComparison.Ordinal))
            return TwitchAuthResult.Failed(TwitchAuthStatus.Error, "State mismatch on the OAuth callback — aborting to be safe.");

        if (!callback.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            return TwitchAuthResult.Failed(TwitchAuthStatus.Error, "Twitch did not return an authorization code.");

        try
        {
            var token = await _api.ExchangeAuthorizationCodeAsync(_options.ClientId, code, verifier, _options.RedirectUri, ct).ConfigureAwait(false);
            var user = await _api.GetUserAsync(token.AccessToken, _options.ClientId, ct).ConfigureAwait(false);
            if (user is null || string.IsNullOrWhiteSpace(user.Id))
                return TwitchAuthResult.Failed(TwitchAuthStatus.Error, "Could not retrieve the Twitch user profile.");

            Persist(token, user);
            return TwitchAuthResult.Ok(string.IsNullOrWhiteSpace(user.DisplayName) ? user.Login : user.DisplayName, user.Id);
        }
        catch (TwitchApiException ex)
        {
            return TwitchAuthResult.Failed(TwitchAuthStatus.Error, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return TwitchAuthResult.Failed(TwitchAuthStatus.Cancelled);
        }
    }

    /// <summary>
    /// Ensures the stored access token is valid for at least <see cref="TwitchOAuthOptions.RefreshBuffer"/>
    /// longer, refreshing it in the background if not. Returns false (and clears the session) if
    /// there is nothing to refresh, or if Twitch rejects the refresh token (e.g. the commander
    /// revoked access from their Twitch settings) — callers should treat that as "logged out" and
    /// prompt <see cref="LoginAsync"/> again.
    /// </summary>
    public async Task<bool> EnsureValidTokenAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Twitch.AccessToken))
            return false;

        if (Twitch.ExpiresAtUtc - _now() > _options.RefreshBuffer)
            return true;

        if (string.IsNullOrWhiteSpace(Twitch.RefreshToken))
            return Twitch.ExpiresAtUtc > _now(); // no refresh token but the current one hasn't expired yet

        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Another caller may have refreshed while we waited for the lock.
            if (Twitch.ExpiresAtUtc - _now() > _options.RefreshBuffer)
                return true;

            var token = await _api.RefreshTokenAsync(_options.ClientId, Twitch.RefreshToken!, ct).ConfigureAwait(false);
            Twitch.AccessToken = token.AccessToken;
            if (!string.IsNullOrWhiteSpace(token.RefreshToken))
                Twitch.RefreshToken = token.RefreshToken; // Twitch rotates refresh tokens on use
            Twitch.ExpiresAtUtc = _now().AddSeconds(token.ExpiresIn);
            if (token.Scopes.Count > 0)
                Twitch.Scopes = token.Scopes.ToList();
            _store.Save(_settings);
            return true;
        }
        catch (TwitchApiException)
        {
            // Refresh token invalid/revoked — clear the session so the app knows to re-prompt login.
            ClearSession();
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Revokes the current token with Twitch (best-effort) and clears the persisted session.</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(Twitch.AccessToken))
        {
            try { await _api.RevokeTokenAsync(_options.ClientId, Twitch.AccessToken!, ct).ConfigureAwait(false); }
            catch { /* best-effort; local state is cleared regardless */ }
        }
        ClearSession();
    }

    private void Persist(TwitchTokenResponse token, TwitchUser user)
    {
        Twitch.AccessToken = token.AccessToken;
        Twitch.RefreshToken = token.RefreshToken;
        Twitch.ExpiresAtUtc = _now().AddSeconds(token.ExpiresIn);
        Twitch.UserId = user.Id;
        Twitch.Username = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Login : user.DisplayName;
        Twitch.Scopes = token.Scopes.ToList();
        _store.Save(_settings);
    }

    private void ClearSession()
    {
        Twitch.AccessToken = null;
        Twitch.RefreshToken = null;
        Twitch.ExpiresAtUtc = default;
        Twitch.UserId = null;
        Twitch.Username = null;
        Twitch.Scopes = new List<string>();
        _store.Save(_settings);
    }

    private string BuildAuthorizeUrl(string codeChallenge, string state)
    {
        var scope = Uri.EscapeDataString(string.Join(' ', _options.Scopes));
        return $"{_options.AuthorizationEndpoint}" +
               $"?client_id={Uri.EscapeDataString(_options.ClientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(_options.RedirectUri)}" +
               $"&response_type=code" +
               $"&scope={scope}" +
               $"&state={Uri.EscapeDataString(state)}" +
               $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
               $"&code_challenge_method=S256";
    }
}
