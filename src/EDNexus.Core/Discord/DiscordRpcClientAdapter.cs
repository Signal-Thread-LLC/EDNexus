using DiscordRPC;
using DiscordRPC.Logging;

namespace EDNexus.Core.Discord;

/// <summary>
/// Wraps <c>DiscordRPC.DiscordRpcClient</c> (the Lachee discord-rpc-csharp package) behind
/// <see cref="IDiscordRpcClient"/>. Every operation is defensive: Discord not being installed, not
/// running, or the local pipe/socket transport being unsupported on this OS must never throw out of
/// this type — it only ever returns false / silently drops the update, per the "graceful fallback"
/// requirement in issue #49.
/// </summary>
public sealed class DiscordRpcClientAdapter : IDiscordRpcClient
{
    private readonly DiscordRpcClient _client;
    private bool _disposed;

    public DiscordRpcClientAdapter(string applicationId)
    {
        _client = new DiscordRpcClient(applicationId)
        {
            // The library can log to its own sink; suppress it entirely rather than pull console/file
            // logging into an app that has its own logging story.
            Logger = new NullLogger(),
        };

        // These fire on a background thread (AutoEvents defaults on) whenever the pipe drops or the
        // handshake fails — swallow them here so a missing/closed Discord client never surfaces as an
        // unhandled exception or a crash loop.
        _client.OnError += (_, _) => { };
        _client.OnConnectionFailed += (_, _) => { };
    }

    public bool TryInitialize()
    {
        if (_disposed) return false;
        try { return _client.Initialize(); }
        catch { return false; }
    }

    public void SetPresence(DiscordPresencePayload payload)
    {
        if (_disposed) return;
        try { _client.SetPresence(ToRichPresence(payload)); }
        catch { /* Discord closed mid-session, pipe error, etc. — never throw. */ }
    }

    public void Clear()
    {
        if (_disposed) return;
        try { _client.ClearPresence(); }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _client.Dispose(); }
        catch { }
    }

    private static RichPresence ToRichPresence(DiscordPresencePayload payload)
    {
        var presence = new RichPresence
        {
            State = Truncate(payload.State),
            Details = Truncate(payload.Details),
        };

        if (payload.LargeImageKey is not null || payload.SmallImageKey is not null)
        {
            presence.Assets = new Assets
            {
                LargeImageKey = payload.LargeImageKey,
                LargeImageText = Truncate(payload.LargeImageText),
                SmallImageKey = payload.SmallImageKey,
                SmallImageText = Truncate(payload.SmallImageText),
            };
        }

        if (payload.StartedAt is { } started)
            presence.Timestamps = new Timestamps { Start = started.UtcDateTime };

        if (payload.Buttons.Count > 0)
        {
            presence.Buttons = payload.Buttons
                .Take(2)   // Discord allows at most two buttons per presence.
                .Select(b => new Button { Label = b.Label, Url = b.Url })
                .ToArray();
        }

        return presence;
    }

    // Discord rejects State/Details longer than 128 bytes; trim well under that so we never fail a
    // whole update over a long system or station name.
    private static string? Truncate(string? text) =>
        text is { Length: > 120 } ? text[..120] : text;
}
