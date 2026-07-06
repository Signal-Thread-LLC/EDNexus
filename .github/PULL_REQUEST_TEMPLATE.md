<!-- Title format: "Phase N - short summary" or "Fix - short summary" -->

## What & why
<!-- What does this change and which problem/issue does it address? -->

Closes #

## Changes
-

## How it was verified
<!-- Commands run + what you observed. Screenshots for UI changes. -->
```sh
dotnet build EDNexus.slnx
dotnet run --project src/EDNexus.Cli -- --once
```

## Checklist
- [ ] Builds clean (`dotnet build EDNexus.slnx`), no new warnings
- [ ] Feature code **reads** `CommanderState`; only `StateTracker` mutates it
- [ ] Platform-specific code sits behind an interface with a graceful fallback
- [ ] Validated against real journal data where relevant
- [ ] No secrets, tokens, or personal journal files committed
