# Exobiology reference data — sources and licensing

Where every number in `Exobio/` came from, what licence (if any) covers that source, and what is
still unverified. Only `*.json` in this folder is embedded (`EDNexus.Core.csproj`), so this file
does not ship.

Last reviewed: **2026-09-14**. Not legal advice — have the licensing position reviewed before release.

## Why this matters for EDNexus specifically

EDNexus's repository is public but has **no licence file**, and packaging declares it proprietary
(`packaging/flatpak/io.github.Signal_Thread_LLC.EDNexus.metainfo.xml`:
`<project_license>LicenseRef-proprietary</project_license>`, with a TODO to set the real licence).
A proprietary product cannot incorporate code or data files distributed under GPL/AGPL. So:

- **Never copy** files, tables or structure from the copyleft projects listed below, and never
  translate their code.
- Facts about the game (a species' payout, "grows only on icy bodies") are not copyrightable in
  themselves, but a wholesale extraction from one compiled source is weaker ground — especially in
  the EU/UK, where a separate database right exists. Prefer primary research, Frontier's own data,
  or our own derivation from observations, and cite them here.

## Data in this repository

| Data | File | Source | Licence of source | Status |
|---|---|---|---|---|
| Species list and codex symbols | `exobiology-species.json` | Journal codex symbols (`ScanOrganic`, `SAASignalsFound`) — Frontier game data | Game data, © Frontier Developments | OK — symbols are what the game emits |
| Vista Genomics payouts | `exobiology-species.json` | [EDMC-BioScan](https://github.com/Silarn/EDMC-BioScan), cross-checked against [StratumFinder](https://github.com/lynnel1/StratumFinder); two conflicts settled against [ed-dsn.net](https://ed-dsn.net/en/vista-genomics-values/) (see PR #107) | BioScan **GPL-2.0**; StratumFinder **AGPL-3.0**; ed-dsn.net no terms stated, and says its list is "taken from the list from Cannon with their autorization" | **Partly re-verified.** Payouts are Frontier's figures (facts), but the first source was a GPL project. On 2026-09-14, the `Value` in the maintainer's own `SellOrganicData` events matched the catalog exactly for all **24 species sold** (of 118). The other 94 still rest on the original sources. |
| Genus sample distance (`sampleDistance`) | `exobiology-species.json` | Community-known colony ranges, entered from general knowledge while implementing issue #96 — no single cited source | — | **Unverified.** Needs a primary citation (Canonn research/forum publication) or in-game confirmation per genus. |
| Spawn rules | `../ExobiologyPredictionRules.cs` | Community Odyssey biology research, written from general knowledge while implementing issue #96. Not copied from any file. | — | **Unverified.** Written in our own schema, but the conditions match those published by the community (Canonn research, and as implemented in EDMC-BioScan). Annotate each rule with a primary citation, or regenerate from observations (below), before release. |

Validation so far: replaying the maintainer's local journals, physics-only prediction included
53/58 DSS-confirmed genera and 45/51 sampled species. Every miss was Brain Tree, which the rules
deliberately never predict without a DSS scan.

## Candidate sources for future work

Researched 2026-09-14 for system-context rules (star class, Guardian zones, nebulae, regions).

| Source | What it offers | Licence / terms found | Usable? |
|---|---|---|---|
| **Our own journals** (`FSDJump`/`Location` `StarPos`, star `Scan` `StarType`, `Parents`, `DistanceFromArrivalLS`, `SAASignalsFound` `Genuses`, `ScanOrganic`, `CodexEntry` `Region`) | System coordinates, star classes, system bodies, confirmed genera/species, codex region when an entry fires | The player's own game output | **Yes** — primary source for runtime context and for validating rules |
| [klightspeed/EliteDangerousRegionMap](https://github.com/klightspeed/EliteDangerousRegionMap) | `findRegion(x, y, z)` → codex region; ships `RegionMapData.json` (~240 KB) and a C# implementation | **MIT** (© 2020 Ben Peddell); README: "Please feel free to use and modify any of these implementations" | **Yes**, with the MIT notice included in third-party notices |
| [Spansh galaxy dumps](https://spansh.co.uk/dumps) (`galaxy.json.gz`, schema at `docs.spansh.co.uk/galaxy.schema.json`) | Every known body's physics, plus `signals.genuses` (genus level only — no species) | **No licence or terms found** on the dumps page or schema | **Not yet.** Would allow deriving genus rules from observations; ask Spansh for permission first. Species-level rules would still need another source. |
| [EDSM nightly dumps](https://www.edsm.net/en/nightly-dumps) | Systems and bodies | **Could not check** — the page returned HTTP 403 to automated fetches | Unknown — check manually |
| Canonn biostats (`/codex/biostats` in [Canonn-GCloud](https://github.com/canonn-science/Canonn-GCloud), backed by a Google Drive JSON) | Observed per-species min/max conditions from EDMC-Canonn submissions | Code is **GPL-3.0**; the data has **no licence stated**. The endpoint returned `{"error": "no spansh data"}` on 2026-09-14. | **Not without permission.** This is Canonn's compiled observation database — ask Canonn. |
| [Canonn research site](https://canonn.science/) ([research policy](https://canonn.science/research-policy/), [guidelines](https://canonn.science/website-guidelines/)) | Published findings (spawn conditions, Guardian site locations) | No data licence. Research policy says results should be "open to all" and posted to the forum. Site states it uses Frontier assets "with the permission of Frontier Developments plc for non-commercial purposes" (that covers the site's assets, not reuse of findings). | **Cite findings as facts**; don't bulk-copy tables. Contact: `webadmin@canonn.science`. |
| [EDMC-BioScan](https://github.com/Silarn/EDMC-BioScan) | Complete rulesets, Guardian zones, tuber zones, nebula reference stars | **GPL-2.0** | **No** — do not copy or port. Useful only to point at the original research it cites. |
| [StratumFinder](https://github.com/lynnel1/StratumFinder) | Payout list | **AGPL-3.0** | **No** |
| [ed-dsn.net Vista Genomics values](https://ed-dsn.net/en/vista-genomics-values/) | Payout table sourced from Canonn | No terms stated | Cross-check only |

## Open actions

1. Re-verify the remaining 94 payouts in `exobiology-species.json` against `SellOrganicData` from
   real journals (24 of 118 done), and replace the BioScan/StratumFinder provenance above.
2. Find a primary citation for each genus's `sampleDistance`.
3. Before extending the rules with system context, either get written permission from Spansh or
   Canonn to derive rules from their data, or derive them from journals plus cited published
   research. Record whichever is chosen here.
4. If the region map is adopted, add its MIT notice to the app's third-party notices.
5. Settle EDNexus's own licence (the packaging TODO). It decides how strict items 1–3 must be.
