# Source and artifact provenance

Date: 2026-09-15

## MAYHEM 1.0 review baseline

The 1.1 public-review tree was derived from the exact previously approved MAYHEM 1.0 Nexus review repository state:

- commit: `704d704acf4b08561da782601def23096b98db60`
- tag: `mayhem-1.0.0-nexus-review-20260902-r3`
- 1.0 review executable SHA-256: `E7D5568DAE4D231F53FB5B116E99AA1CC972B077EE8DA3936AD541DA39FBB6F9`

The 1.0 repository was copied into a separate `MAYHEM-1.1.0` review workspace before any 1.1 change. The approved 1.0 repository itself was not modified.

## Intentional 1.1 source delta

The gameplay EXE patch manifest and existing patch engine remain unchanged.

Intentional runtime changes for 1.1 are limited to:

- `src/ACOdysseyUMM/ACOdysseyUMM.csproj`: version `1.1.0` and embedded Forge delta resource;
- `src/ACOdysseyUMM/MainForm.cs`: coordinated Forge verification/apply/restore integration;
- `src/ACOdysseyUMM/ForgeLevel255Manager.cs`: deterministic local Forge reconstruction, validation, backup, recovery and restore logic;
- `src/ACOdysseyUMM/InternalsVisibleTo.cs`: test visibility only;
- `src/ACOdysseyUMM/Assets/forge-levels255-v1.mfd.br`: frozen deterministic Forge delta.

The existing `patches.json`, `PatchEngine.cs`, gameplay selection code, installer artwork/audio and application icon were not intentionally changed for the 1.1 feature fix.

## Why the Forge layer was added

MAYHEM 1.0 correctly allowed Mercenary levels above 99 in the executable-level hierarchy/selector path, but the game's EnemyRankInfo progression vectors in `DataPC_patch_01.forge` remained at the vanilla 99-record length.

The game's level-based lookup clamps an index to the available record count. Therefore levels above 99 still consumed the level-99 stat record when the Forge remained vanilla.

The 1.1 Forge output extends the relevant EnemyRankInfo progression data to 255 records so the existing game lookup can select distinct records for levels 100 through 255.

## Forge generation provenance

The canonical modified Forge was generated during development from the user's exact supported vanilla archive using 233 validated EnemyRankInfo replacements.

Batch validation results:

- generated replacements: `233/233 PASS`
- serialized/mutation round trips: `233/233 PASS`
- relevant Mercenary RankInfo profiles included: 24

Canonical vanilla Forge:

- size: `3259897607` bytes
- SHA-256: `9E6F14A85B64B4C39683786D50794A714F50DDDCB343B9312A728806AF468D0E`

Canonical 255-stat Forge:

- size: `3260776448` bytes
- SHA-256: `D80B46495C6669DF2BCB2CA674227F8FB646A0BE196502C22770CCB5CB6984AF`

A compact deterministic COPY/LITERAL delta was generated between those two exact file identities and Brotli-compressed for embedding:

- file: `forge-levels255-v1.mfd.br`
- size: `3896382` bytes
- SHA-256: `6771FA43AA0DBEEBE3D9E87448E86EE1C1C18A1096780B4431D49743A34D28CC`

Independent delta verification reconstructed the canonical target Forge from the canonical vanilla Forge and reproduced the exact `D80B4649...` SHA-256.

The release installer contains only this compact delta, not either complete Ubisoft Forge archive and not the development Forge-builder toolchain.

## Installer integration validation

Before preparing the public review tree, the 1.1 implementation was exercised on isolated copies of the canonical vanilla `ACOdyssey.exe` and `DataPC_patch_01.forge`.

Results:

- all 12 published option combinations: Apply / Verify / Restore PASS;
- all Light/Linear combinations produced the exact `D80B4649...` extended Forge;
- all Off combinations required/retained the exact vanilla `9E6F14A8...` Forge;
- every Restore returned the EXE and Forge copies to their exact vanilla SHA-256 identities;
- existing EXE patch output identities remained equal to the previously validated patch outputs;
- no undeclared EXE bytes changed;
- explicit MAYHEM 1.0 Level Unlock migration test passed: legacy patched EXE + vanilla Forge -> 1.1 Restore -> exact vanilla pair -> 1.1 Linear Apply -> extended Forge -> Restore -> exact vanilla pair.

The currently available machine does not have the full game installed, so the 1.1 build cannot additionally be launched into a live Odyssey gameplay session at review-preparation time. File-level integration and the stat-data path were verified independently.

## Final 1.1 review artifact

Filename:

`aco_mayhem_installer.exe`

Identity:

- size: `183910389` bytes
- SHA-256: `AD13D7543CC503C25B3AAA200E03178B58F27CAFAEF6B1A327834CFB8D78A739`
- FileVersion: `1.1.0.0`
- ProductVersion: `1.1.0`

A separate reconstruction from the isolated public-review source produced the same byte length and SHA-256 as the release candidate.

The final Git commit and 1.1 review tag are recorded after the repository is frozen, because a commit cannot contain its own final commit SHA without changing that SHA.
