# EDNexus.Ebs — Twitch Extension Backend Service

A small, standalone ASP.NET Core service that bridges the EDNexus desktop app (running on the
broadcaster's machine) and the Twitch Extension frontend (running in viewers' browsers). The
desktop app cannot talk to the extension frontend directly (CORS, TLS, and firewall constraints on
a sandboxed iframe), so it POSTs state updates here; the EBS verifies the caller and relays the
update to viewers via Twitch's Extensions PubSub API.

The EBS is also the desktop app's *only* path to Twitch login: it is the registered Twitch
application (holds the client id + secret), performs the real Twitch OAuth 2.0 Authorization Code
handshake on the broadcaster's behalf, and issues its own long-lived opaque token back to the
desktop client. The desktop app never talks to `*.twitch.tv` directly and never sees a raw Twitch
access/refresh token — see "OAuth login flow" below.

The extension frontend that consumes these updates lives in [`extension/`](../../extension/README.md),
along with the payload contract it expects.

## Running locally

```sh
dotnet run --project src/EDNexus.Ebs
```

Configuration can be supplied via `appsettings.json`, `appsettings.Development.json` (git-ignored),
environment variables, or `dotnet user-secrets` — standard ASP.NET Core configuration precedence
applies.

| Setting | Environment variable | Description |
|---|---|---|
| `Twitch:ClientId` | `Twitch__ClientId` | Twitch application Client ID (used for both the OAuth login flow and PubSub). |
| `Twitch:ClientSecret` | `Twitch__ClientSecret` | Twitch application Client Secret, used for the `/oauth/authorize` + `/oauth/callback` Authorization Code flow. **Never commit this.** |
| `Twitch:OAuthRedirectUri` | `Twitch__OAuthRedirectUri` | The EBS's own redirect URI, exactly as registered in the Twitch Developer Console (e.g. `https://ebs.example.com/oauth/callback`). |
| `Twitch:OAuthScopes` | `Twitch__OAuthScopes__0`, `...__1`, ... | Scopes requested from Twitch during login. Default `user:read:email`. |
| `Twitch:ExtensionId` | `Twitch__ExtensionId` | The Twitch Extension's Client ID. |
| `Twitch:ExtensionSecret` | `Twitch__ExtensionSecret` | Base64-encoded Extension Secret from the Twitch Developer Console. **Never commit this.** |
| `Ebs:Port` | `Ebs__Port` | HTTP port Kestrel listens on when `ASPNETCORE_URLS` isn't set. Default `8787`. |
| `Ebs:MaxStatePayloadBytes` | `Ebs__MaxStatePayloadBytes` | Max serialized state size forwarded to PubSub. Default `5000` (Twitch's hard limit is 5 KiB). |
| `Ebs:UpdateStateRateLimit` / `Ebs:UpdateStateRateLimitWindowSeconds` | `Ebs__UpdateStateRateLimit` / `Ebs__UpdateStateRateLimitWindowSeconds` | Per-channel rate limit applied to `POST /api/update-state`. Default 1 request / 2 seconds. |
| `Ebs:OAuthSessionTtlMinutes` | `Ebs__OAuthSessionTtlMinutes` | How long a commander has to complete the Twitch consent page before the login session expires. Default 10 minutes. |
| `Ebs:OAuthCodeTtlSeconds` | `Ebs__OAuthCodeTtlSeconds` | How long the one-time authorization code handed to the desktop client is redeemable at `/oauth/token`. Default 60 seconds. |
| `Ebs:TwitchTokenRefreshIntervalMinutes` / `Ebs:TwitchTokenRefreshBufferMinutes` | `Ebs__TwitchTokenRefreshIntervalMinutes` / `Ebs__TwitchTokenRefreshBufferMinutes` | How often the background loop checks broadcasters' Twitch grants, and how far ahead of expiry it refreshes them. Defaults 30 / 60 minutes. |

## OAuth login flow

The desktop app never talks to Twitch directly. Instead:

1. **`GET /oauth/authorize`** — the desktop app opens the commander's browser here with its own
   loopback `redirect_uri`, a CSRF `state`, and a PKCE `code_challenge` (`code_challenge_method=S256`).
   The EBS records these against a short-lived session and redirects the browser to Twitch's real
   consent page, using the EBS's own registered `redirect_uri` and the session id as Twitch's `state`.
2. **`GET /oauth/callback`** — Twitch redirects back here after the commander approves/denies. The
   EBS exchanges the code for a Twitch token grant using its client secret, looks up the
   broadcaster's Twitch identity, and redirects the browser to the desktop's original loopback
   `redirect_uri` with a one-time authorization `code` and the desktop's own `state` — never a raw
   Twitch token.
3. **`POST /oauth/token`** — the desktop's loopback listener has the code; it exchanges it here
   (`{ "code", "code_verifier", "redirect_uri" }`), proving possession of the PKCE verifier. On
   success the EBS mints a long-lived opaque token mapped server-side to the channel id and the
   underlying Twitch access/refresh tokens, and returns `{ "token", "channelId", "username" }`. This
   is the only token the desktop client ever stores.
4. **`POST /oauth/revoke`** — best-effort logout: `Authorization: Bearer <ebs-token>` revokes both
   the EBS token and (best-effort) the underlying Twitch grant.

A background service refreshes each broadcaster's Twitch access token ahead of expiry using their
stored refresh token, so the commander stays logged in across a multi-day gap without re-auth. If a
refresh ever fails (e.g. the commander revoked access on Twitch), the broadcaster's EBS token is
marked invalid and `/api/update-state` starts rejecting it with `401`, so the desktop client knows
to prompt login again.

## API

### `POST /api/update-state`

Called by the desktop client on behalf of the broadcaster.

- **Auth**: `Authorization: Bearer <ebs-token>` — the long-lived, per-broadcaster token minted by
  `POST /oauth/token` above. The EBS resolves it server-side to a channel id; the channel id is
  never taken from the request body, so a compromised client cannot spoof another broadcaster's
  channel. Returns `401` if the token is unknown or its underlying Twitch grant has been marked
  invalid (prompting the desktop client to log in again).
- **Body**: `{ "state": { ... arbitrary JSON state payload ... } }`
- **Behavior**: validates the serialized state is under the configured size limit, caches it for
  `GET /api/initial-state`, and forwards it to `POST https://api.twitch.tv/helix/extensions/pubsub`
  signed with a short-lived "external" role JWT minted from the Extension Secret (the Extensions
  platform's own JWT scheme — unrelated to broadcaster authentication described above).
- **Responses**: `200 OK` on success, `401 Unauthorized` for a missing/invalid/revoked token,
  `413 Payload Too Large` if the state exceeds the size limit, `429 Too Many Requests` if
  the per-channel rate limit is exceeded, `502 Bad Gateway` if Twitch PubSub rejects the message.

### `GET /api/initial-state/{channelId}`

Called by the extension frontend on load so it doesn't have to wait for the next PubSub event.
Returns the last state payload published for that channel, or `404` if none has been published yet.
Unauthenticated but rate-limited per caller IP.

### `GET /healthz`

Liveness probe for container/serverless hosting.

## Deployment

A `Dockerfile` is included for containerized hosting; the service is also small enough to host on
a serverless container platform (Azure Container Apps, Fly.io, etc.). **Production caveat:** both
`IChannelStateStore` and `IBroadcasterTokenStore` currently ship with in-memory implementations,
sufficient for a single EBS instance and for local development/testing. A multi-instance deployment
— or any deployment where losing broadcaster tokens on a restart is unacceptable — needs a real
persistent, shared backing store (e.g. a database or Redis) for `IBroadcasterTokenStore` in
particular, since it holds the long-lived credentials and Twitch refresh tokens broadcasters rely on
to stay logged in across days.
