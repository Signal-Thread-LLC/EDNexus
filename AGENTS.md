# AGENTS.md

Operating guide for humans and AI agents working on **EDNexus**. Read this before making changes.

## What this is

A single cross-platform desktop app that replaces the sprawl of Elite Dangerous companion tools
(market, routes, exobiology, colonisation, materials). It is driven entirely off the game's own
data — no memory reading, no injection.

## Build / run / test

```sh
# Build everything (note: .NET 10 uses the .slnx solution format)
dotnet build EDNexus.slnx

# Run the desktop app
dotnet run --project src/EDNexus.App

# Headless engine harness — replays the latest journal and prints commander state.
# This is the fastest way to validate engine/feature changes against real data.
dotnet run --project src/EDNexus.Cli -- --once
```

There is no test project yet; the CLI `--once` harness is the current validation path. When you add
`EDNexus.Tests`, wire it into the same commands here.

## Architecture

```
Journal.*.log + *.json  ──►  JournalWatcher  ──►  JournalEventBus  ──►  StateTracker  ──►  CommanderState
                                                        │                                        │
                                             feature modules subscribe                 UI / overlay / voice read
```

| Project | Role |
|---|---|
| `src/EDNexus.Core` | Engine + feature services. No UI dependencies. |
| `src/EDNexus.App` | Avalonia 12 desktop UI (MVVM via CommunityToolkit.Mvvm). |
| `src/EDNexus.Cli` | Headless harness for validation. |

Key types live in `src/EDNexus.Core`: `JournalWatcher`, `JournalEntry`, `JournalEventBus`,
`StateTracker`, `CommanderState`, `EngineHost`.

## Conventions (do not violate without discussion)

1. **One writer.** Only `StateTracker` mutates `CommanderState`. Feature modules and the UI *read*
   it. New event handling goes through `StateTracker` or a dedicated feature service that owns its
   own derived state.
2. **Parse defensively.** Journal events change between game updates. Read fields off the raw
   `JsonElement` via `JournalEntry` helpers; never assume a field exists. Unknown events must not
   throw — the bus isolates handler errors, but don't rely on that.
3. **Prefer `_Localised` names** for anything shown to the user (`GetLocalised(...)`).
4. **Cross-platform first.** Target behaviour that works on Windows, Linux, and macOS. Anything
   OS-specific (overlay, native TTS) goes behind an interface with a no-op fallback.
5. **Never commit** real journal files, tokens, or `bin/`/`obj/`.
6. **Match surrounding style** — nullable enabled, file-scoped namespaces, XML docs on public types.

## How work is organised

- The roadmap lives in the **Epic** (issue #7) and per-phase issues, each tied to a milestone.
- Each phase ships on a branch → PR → `main`. Reference the issue with `Closes #N`.
- Tasks labelled **`agent-ready`** are scoped tightly enough to hand to an agent; use the
  **🤖 Agent task** issue form to create more.

## Definition of done

- Builds clean with no new warnings.
- Validated against real journal data (via the app or `--once` harness).
- Honours the conventions above.
- PR description says what was verified and how.
