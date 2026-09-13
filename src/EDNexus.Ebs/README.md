# EDNexus.Ebs — Twitch Extension Backend Service

A small, standalone ASP.NET Core service that bridges the EDNexus desktop app (running on the
broadcaster's machine) and the Twitch Extension frontend (running in viewers' browsers). The
desktop app cannot talk to the extension frontend directly (CORS, TLS, and firewall constraints on
a sandboxed iframe), so it POSTs state updates here; the EBS verifies the caller and relays the
update to viewers via Twitch's Extensions PubSub API.

This service has no dependency on the desktop OAuth flow or state-broadcaster tickets (#41/#42) —
it is a standalone, independently deployable API with the contract described below.

## Running locally

```sh
dotnet run --project src/EDNexus.Ebs
```

Configuration can be supplied via `appsettings.json`, `appsettings.Development.json` (git-ignored),
environment variables, or `dotnet user-secrets` — standard ASP.NET Core configuration precedence
applies.

| Setting | Environment variable | Description |
|---|---|---|
| `Twitch:ClientId` | `Twitch__ClientId` | Twitch application Client ID. |
| `Twitch:ExtensionId` | `Twitch__ExtensionId` | The Twitch Extension's Client ID. |
| `Twitch:ExtensionSecret` | `Twitch__ExtensionSecret` | Base64-encoded Extension Secret from the Twitch Developer Console. **Never commit this.** |
| `Ebs:Port` | `Ebs__Port` | HTTP port Kestrel listens on when `ASPNETCORE_URLS` isn't set. Default `8787`. |
| `Ebs:MaxStatePayloadBytes` | `Ebs__MaxStatePayloadBytes` | Max serialized state size forwarded to PubSub. Default `5000` (Twitch's hard limit is 5 KiB). |
| `Ebs:UpdateStateRateLimit` / `Ebs:UpdateStateRateLimitWindowSeconds` | `Ebs__UpdateStateRateLimit` / `Ebs__UpdateStateRateLimitWindowSeconds` | Per-channel rate limit applied to `POST /api/update-state`. Default 1 request / 2 seconds. |

## API

### `POST /api/update-state`

Called by the desktop client on behalf of the broadcaster.

- **Auth**: `Authorization: Bearer <twitch-extension-jwt>` — a JWT issued by Twitch to the
  broadcaster's extension configuration/config-page session, signed with the Extension Secret and
  carrying `role: "broadcaster"` and a `channel_id` claim. The EBS verifies the signature, expiry,
  and role; the channel id is taken from the verified token, never from the request body, so a
  compromised client cannot spoof another broadcaster's channel.
- **Body**: `{ "state": { ... arbitrary JSON state payload ... } }`
- **Behavior**: validates the serialized state is under the configured size limit, caches it for
  `GET /api/initial-state`, and forwards it to `POST https://api.twitch.tv/helix/extensions/pubsub`
  signed with a short-lived "external" role JWT minted from the same Extension Secret.
- **Responses**: `200 OK` on success, `401 Unauthorized` for a missing/invalid/non-broadcaster
  token, `413 Payload Too Large` if the state exceeds the size limit, `429 Too Many Requests` if
  the per-channel rate limit is exceeded, `502 Bad Gateway` if Twitch PubSub rejects the message.

### `GET /api/initial-state/{channelId}`

Called by the extension frontend on load so it doesn't have to wait for the next PubSub event.
Returns the last state payload published for that channel, or `404` if none has been published yet.
Unauthenticated but rate-limited per caller IP.

### `GET /healthz`

Liveness probe for container/serverless hosting.

## Deployment

A `Dockerfile` is included for containerized hosting; the service is also small enough to host on
a serverless container platform (Azure Container Apps, Fly.io, etc.) — it has no local state beyond
an in-memory per-channel cache, so any platform that can run a stateless ASP.NET Core container
works. A multi-instance deployment should back `IChannelStateStore` with a shared cache (e.g. Redis)
instead of the default in-memory implementation.
