# EDNexus Twitch Extension — commander card

The viewer-facing half of EDNexus's Twitch integration: a **flyout card** drawn over the stream.
Collapsed, it is a small tab on the left edge showing one line ("Docked at Jameson Memorial").
Clicking it slides out a panel with the commander's ship, location, ranks, exobiology progress,
missions, mining and cargo; closing it puts the stream back in full view. Collapsed is the default,
and each viewer's choice is remembered locally.

```
EDNexus desktop app                 EDNexus.Ebs                    this extension
─────────────────────               ───────────                    ──────────────
TwitchStreamCardService  ──POST──►  /api/update-state  ──PubSub──►  js/state.js ──► js/card.js
  (StreamCardSnapshot)               (relays to Twitch)             /api/initial-state (on load)
```

## Layout

| Path | Role |
|---|---|
| `video_overlay.html` | The overlay itself — the handle plus the panel. |
| `config.html` | Broadcaster config page (which EBS this channel reads from). |
| `css/card.css` | Overlay styling. Click-through everywhere the card is not. |
| `css/config.css` | Config page styling. |
| `js/state.js` | Transport: initial-state fetch + PubSub subscription. No rendering. |
| `js/card.js` | Rendering and flyout behaviour. No network. |
| `dev/sample-state.json` | A representative payload for local development. Not needed in the upload. |

## Developing locally

No Twitch and no EBS required — the card falls back to the bundled sample when the Twitch helper is
absent, or whenever `?mock=1` is in the query string:

```bash
python -m http.server 8931 --directory extension
```

Then open <http://127.0.0.1:8931/video_overlay.html?mock=1>.

To drive it from a real EBS instead, run the EBS (`dotnet run --project src/EDNexus.Ebs`), point the
desktop app's `Twitch:EbsBaseUrl` at it, and load the extension through the
[Twitch Developer Rig](https://dev.twitch.tv/docs/extensions/rig/). Add the Rig's origin to
`Ebs:AdditionalAllowedFrontendOrigins` so `GET /api/initial-state/{channelId}` passes CORS.

## Publishing to Twitch

The extension is configured in the [Twitch Developer Console](https://dev.twitch.tv/console/extensions),
not by a file in this folder. Settings that matter:

- **Type**: Video — Fullscreen *and* Video — Overlay. The card positions itself against the left edge
  of the player in both.
- **Viewer path**: `video_overlay.html`. **Config path**: `config.html`.
- **Testing base URI**: your local server while developing; the asset upload is used once hosted.
- **Required Broadcaster Abilities**: none. The card only reads.
- The extension's **Client ID** and **Secret** go into the EBS as `Twitch:ExtensionId` and
  `Twitch:ExtensionSecret` — see `src/EDNexus.Ebs/README.md`.

Zip the contents of this folder (`dev/` can be left out) and upload it as the extension's asset
bundle.

## The payload

Produced by `EDNexus.Core.Twitch.StreamCardSnapshot` and relayed verbatim. Twitch caps a PubSub
message at 5 KiB, so keys are short and lists are capped app-side.

```jsonc
{
  "v": 1,                       // schema version — StreamCardSchema.Version
  "at": "2026-09-15T18:20:00Z", // when the snapshot was taken; drives the "stale" note
  "headline": "Nervi / Nervi 2 a",
  "subline": "Flying Krait Phantom (PH-01)",
  "cmdr":     { "name": …, "credits": …, "ranks": [{ "label", "name", "pct", "elite" }] },
  "ship":     { "type", "name", "ident", "fuel", "fuelMax", "cargo", "jump" },
  "loc":      { "system", "body", "docked", "station", "stationType" },
  "carrier":  { "name", "callsign", "fuel", "jump", "pendingSystem", "departsAt" },
  "exo":      { "pendingValue", "pendingCount", "soldValue", "soldCount", "firsts",
                "genus", "species", "samples", "body", "signals" },
  "mining":   { "content", "motherlode", "remaining", "materials": […], "prospected", "refined" },
  "missions": { "active", "cap", "reward", "stacks": [{ "target", "faction", "count", "kills", "reward" }] },
  "cargo":    [ { "name", "t" } ]
}
```

**Every section is optional.** A broadcaster opts in per section in the desktop app
(Settings → Twitch), and anything switched off is never serialized — it does not leave their
machine, so there is nothing for this frontend to hide. Treat a missing section as "not shown", and
never assume a field is present. `dev/sample-state.json` is a complete example.

### Changing the contract

`v` is the compatibility gate. A frontend that sees a `v` higher than `SUPPORTED_SCHEMA` (in
`js/state.js`) renders a "newer EDNexus" notice rather than a half-parsed card — viewers update
whenever the broadcaster does, but the reverse is not true, so:

- **adding** an optional field needs no version bump (older frontends ignore it);
- **removing or repurposing** one is a bump, in both `StreamCardSchema.Version` and
  `SUPPORTED_SCHEMA`.

## Conventions

- **No inline script or style.** Twitch's extension CSP rejects both; everything lives in `css/` and
  `js/`.
- **No `innerHTML` with payload data.** The snapshot carries commander-authored strings (ship name,
  carrier name, mission targets) that originate in the game and pass through the broadcaster's
  machine before landing in every viewer's browser. `js/card.js` builds nodes and sets
  `textContent`; keep it that way.
- **The overlay must stay click-through.** The iframe covers the whole player, so `body` is
  `pointer-events: none` and only the handle and the open panel re-enable it.
- **Vanilla JS, no build step.** What is in this folder is what gets uploaded.
