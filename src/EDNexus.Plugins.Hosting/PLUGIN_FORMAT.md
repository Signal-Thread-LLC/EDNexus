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

Set `EDNEXUS_PLUGINS_DIR` to use a different folder while developing (mirrors
`EDNEXUS_JOURNAL_DIR`). It does not need to exist yet.

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
| `name` | yes | Display name, ≤ 100 chars, no control characters. |
| `version` | yes | The plugin's own version, strict [SemVer 2.0.0](https://semver.org) (`1.2.0`, `2.0.0-beta.1`). |
| `author` | no | ≤ 200 chars. |
| `description` | no | ≤ 2000 chars; newlines allowed. |
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
Installing validates the whole package, extracts it to a staging folder, and moves it to
`<plugins root>/<id>/`; an already-installed id is rejected unless replacement is requested.

A package is **rejected** if any entry:

- is absolute (`/x`, `C:\x`, `\\server\x`), contains `..` or `.` segments, empty segments, `:`,
  `<>"|?*`, control characters, or a backslash-normalised form of any of these (zip-slip);
- has a segment that is a Windows device name (`CON`, `nul.txt`, `COM1`…) or ends in a dot/space;
- is longer than 240 characters or nested deeper than 16 levels;
- duplicates another entry case-insensitively, or nests under a path that is a file;
- is a symbolic link or encrypted;

or if the package exceeds the size limits (`PluginPackageLimits`; defaults: 64 MiB package,
2,000 entries, 128 MiB per file, 256 MiB total uncompressed, 100:1 compression ratio for files over
1 MiB). Sizes are re-checked against the actual decompressed bytes during extraction. The manifest
must be named exactly `plugin.json`, be valid, and its `entryAssembly` must be in the package with
the same casing.
