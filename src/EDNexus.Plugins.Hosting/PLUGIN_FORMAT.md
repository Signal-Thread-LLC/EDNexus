# EDNexus plugin format

This describes the on-disk format of an EDNexus plugin: the `plugin.json` manifest, the folder
layout, and the `.ednplugin` package. It is implemented by `PluginManifestParser`,
`PluginPackage`, `PluginInstaller` and `PluginPaths` in this project (issue #54). Loading,
lifecycle and capability enforcement are covered by the plugin host issues (#55–#57).

## Where plugins live

Each installed plugin is a folder named after its id under the per-user plugins root, alongside
`settings.json` — **never** in the (read-only) install directory:

| OS | Plugins root |
|---|---|
| Windows | `%LOCALAPPDATA%\EDNexus\plugins\` |
| Linux | `~/.local/share/EDNexus/plugins/` (Flatpak: `~/.var/app/<app-id>/data/EDNexus/plugins/`) |
| macOS | `~/Library/Application Support/EDNexus/plugins/` |

Set `EDNEXUS_PLUGINS_DIR` to an **absolute** path to use a different folder while developing
(mirrors `EDNEXUS_JOURNAL_DIR`). It does not need to exist yet; relative values are ignored so the
root can never depend on the current directory.

```
plugins/
  com.acme.jumpcounter/
    plugin.json
    Acme.JumpCounter.dll        <- entryAssembly
    SomeDependency.dll
```

## `plugin.json`

UTF-8 JSON (a BOM, `//` comments and trailing commas are tolerated), at most 64 KiB. Property
names are matched case-insensitively; **unknown properties are ignored** so newer manifests load
on older hosts. A property given more than once is an error.
See [`plugin.sample.json`](plugin.sample.json) for a complete example.

| Field | Required | Rules |
|---|---|---|
| `id` | yes | Lowercase reverse-DNS, ≥ 2 dot-separated segments of `[a-z0-9_-]` (each starting and ending with a letter/digit), ≤ 128 chars. Must be unique; it is also the install folder name. e.g. `com.acme.jumpcounter` |
| `name` | yes | Display name, ≤ 100 chars, no control or invisible formatting characters (bidi overrides, zero-width spaces/joiners). |
| `version` | yes | The plugin's own version, strict [SemVer 2.0.0](https://semver.org) (`1.2.0`, `2.0.0-beta.1`). |
| `author` | no | ≤ 200 chars; same character rules as `name`. |
| `description` | no | ≤ 2000 chars; newlines and tabs allowed, otherwise the same character rules as `name`. |
| `sdkVersion` | yes | The `EDNexus.Plugins.Abstractions` contract version built against, `"major.minor"` (currently `"1.0"`). Compatible with any host SDK of the **same major** and an **equal or newer minor** (`PluginSdk.IsCompatible`). |
| `minAppVersion` | no | Minimum EDNexus app version, SemVer. |
| `entryAssembly` | yes | Path of the entry `.dll` relative to the plugin folder, `/`-separated (safe-path rules below). |
| `entryType` | yes | Full CLR type name implementing `IEDNexusPlugin`, e.g. `Acme.JumpCounter.JumpCounterPlugin` (nested types use `+`). |
| `capabilities` | no | Array drawn from `events`, `state`, `ui.dashboard`, `ui.overlay`, `storage`, `network` (exact, lowercase). Any other value rejects the manifest; duplicates are collapsed. Plugins that make network calls must declare `network`. |

An invalid manifest is rejected with every reason listed (e.g. `'version' "1.0" is not valid
SemVer 2.0.0`). Wrong types, missing required fields, and malformed JSON never throw.

When several plugins are discovered, `PluginManifestParser.FindDuplicateIds` reports ids that
occur more than once (case-insensitively); none of the duplicates should be loaded.
`PluginCompatibility.Check` gates `sdkVersion` / `minAppVersion` against the running host.

## `.ednplugin` packages

A `.ednplugin` is a zip of the plugin folder. `plugin.json` must be at the archive root, or inside
a single top-level folder that wraps everything (what "compress folder" tools produce).
Installing validates the whole package, extracts it to a staging folder (`.staging-<guid>`), and
moves it to `<plugins root>/<id>/`; an already-installed id is rejected unless replacement is
requested. A replace first renames the old version to `.replaced-<id>.<guid>`; if the swap fails
it is moved back (or, if that also fails, the error names where it was preserved). The host calls
`PluginInstaller.RecoverInterrupted(root)` at startup to finish any replace cut short by a crash:
it restores a backup whose `<id>` folder is missing, deletes backups that were superseded, and
deletes stale staging folders. Folder names starting with `.` are never valid plugin ids, so
discovery ignores them.

A package is **rejected** if any entry:

- is absolute (`/x`, `C:\x`, `\\server\x`), contains `..` or `.` segments, empty segments, `:`,
  `<>"|?*`, control characters, invisible formatting characters (e.g. `evil` + U+202E + `lld.exe`,
  which displays as `evilexe.dll`), unpaired surrogates, or a backslash-normalised form of any of these
  (zip-slip);
- has a segment that is a Windows device name (`CON`, `nul.txt`, `COM1`…) or ends in a dot/space;
- is longer than 240 characters or nested deeper than 16 levels;
- duplicates another entry case-insensitively after Unicode NFC normalisation (so `café` spelled
  precomposed and decomposed collide, as they do on macOS), or nests under a path that is a file;
- is a symbolic link or encrypted;

or if the package exceeds the size limits (`PluginPackageLimits`; defaults: 64 MiB package,
2,000 entries, 128 MiB per file, 256 MiB total uncompressed, 100:1 compression ratio for files over
1 MiB). The entry count is read from the End Of Central Directory record before any entries are
parsed. During extraction, sizes are re-checked against the actual decompressed bytes and every
file's CRC-32 is verified. The manifest must be named exactly `plugin.json`, be valid, and its
`entryAssembly` must be in the package with the same casing.
