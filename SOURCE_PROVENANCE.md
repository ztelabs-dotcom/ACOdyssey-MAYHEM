# Source and artifact provenance

Date: 2026-09-17

## MAYHEM 1.1 review baseline

The MAYHEM 1.2 public-review tree is derived from the exact frozen 1.1 Nexus review repository state:

- commit: `0a5ae786dcb7f686d16d489330104ce299f9c5cb`
- tag: `mayhem-1.1.0-nexus-review-20260915`
- 1.1 review executable size: `183910389` bytes
- 1.1 review executable SHA-256: `AD13D7543CC503C25B3AAA200E03178B58F27CAFAEF6B1A327834CFB8D78A739`

The 1.1 tag remains unchanged and permanently identifies the source previously submitted for Nexus review.

## Intentional 1.2 source delta

The intentional runtime changes for 1.2 are limited to the following areas.

### Existing files changed

- `src/ACOdysseyUMM/ACOdysseyUMM.csproj`: version `1.2.0`;
- `src/ACOdysseyUMM/ForgeLevel255Manager.cs`: Forge transaction I/O optimization, transaction-scoped file locking and measured progress reporting;
- `src/ACOdysseyUMM/InternalsVisibleTo.cs`: test visibility update;
- `src/ACOdysseyUMM/MainForm.cs`: Ubisoft dual-executable integration and installation progress UI;
- `src/ACOdysseyUMM/PatchEngine.cs`: reusable target/state-file support required for a second managed executable;
- `src/ACOdysseyUMM/PatchModels.cs`: additional transaction/state models;
- `src/ACOdysseyUMM/patches.json`: exact Ubisoft Connect primary and plus executable target definitions.

### New files

- `src/ACOdysseyUMM/UbisoftDualExeManager.cs`: coordinates the supported Ubisoft `ACOdyssey.exe` and `ACOdyssey_plus.exe` pair with the Forge transaction;
- `src/ACOdysseyUMM/InstallProgress.cs`: deterministic progress reporting used by the installer UI and long-running Forge operations.

### Runtime inputs intentionally unchanged

The following files are byte-identical to the frozen 1.1 review inputs:

- `src/ACOdysseyUMM/Assets/forge-levels255-v1.mfd.br`
- `src/ACOdysseyUMM/Assets/IconHelmetSource.png`
- `src/ACOdysseyUMM/Assets/InstallerArt.png`
- `src/ACOdysseyUMM/Assets/InstallerAudio.wav`
- `src/ACOdysseyUMM/Assets/Mayhem.ico`
- `src/ACOdysseyUMM/Assets/MayhemIconPreview.png`

The Forge delta identity remains:

- size: `3896382` bytes
- SHA-256: `6771FA43AA0DBEEBE3D9E87448E86EE1C1C18A1096780B4431D49743A34D28CC`

Gameplay option selection code and unrelated installer support code remain unchanged where not required by the 1.2 platform integration.

## Why Ubisoft Connect support was added

MAYHEM 1.0 and 1.1 published only the validated Steam executable identity even though the mod logic itself was not conceptually Steam-specific.

For 1.2, exact Ubisoft Connect 1.5.6 binaries were obtained from a legitimate user installation and mapped against the frozen production MAYHEM patch set.

Supported Ubisoft primary identity:

- `ACOdyssey.exe`
- size: `285838672` bytes
- SHA-256: `3CB92F72823DB2C5EC24B77ADCD2325C9E1C61DBDB3E7EEA87151374F49B1A07`
- PE timestamp: `0x6198213B`

Supported Ubisoft sibling identity:

- `ACOdyssey_plus.exe`
- size: `501398864` bytes
- SHA-256: `422439DA0C0F282B29C6C17F3BDC7B3D81B624B7ACEC2B9C69AED2CA22896560`
- PE timestamp: `0x61983561`

The Ubisoft primary executable is not globally byte-identical to Steam. MAYHEM-specific compatibility was validated separately. The production patch preimages, relevant file-offset/RVA topology, helper targets and required executable ranges were validated for the exact Ubisoft primary build.

`ACOdyssey_plus.exe` required a separate mapping because much of the relevant gameplay code is relocated relative to the Steam/plain executable family. All production MAYHEM components were mapped, relative/RIP targets were regenerated for the plus binary and all published option compositions were statically validated for conflicts and exact preimages.

## Ubisoft runtime validation

Before 1.2 integration, the full plus-family candidate for:

- Mercenary Level Unlock: Linear
- Hunter Pressure: x6
- Selector255
- Fast Travel redispatch: 1 second
- Immediate Recycle

produced this patched plus-family executable identity:

`3275F9F87D304A6F9AD1A9B32884B0DB9AC267DDEEFBB937D05931DEF52D6998`

The candidate was exercised in a live Ubisoft Connect installation. Observed runtime results included:

- Mercenary levels above 99;
- extended combat/stat scaling above level 99;
- Hunter Pressure x6 behavior;
- Immediate Recycle behavior;
- approximately one-second Fast Travel redispatch.

The 1.2 regression harness later reproduced the exact same patched plus-family SHA-256 for the equivalent full selection.

## Ubisoft dual-executable policy

The live Ubisoft installation contains both supported executable families, while the launcher can mediate execution in ways that are not obvious to an end user.

For this reason, MAYHEM 1.2 does not ask the user to choose which Ubisoft executable is authoritative. When the exact supported Ubisoft primary is selected, the installer requires and manages both sibling executables as one platform state.

Apply / Verify / Restore coordinate:

1. `ACOdyssey.exe`
2. `ACOdyssey_plus.exe`
3. `DataPC_patch_01.forge`

Interrupted dual-transaction recovery was exercised against partial-commit states and returned the managed set to exact vanilla identities.

## Forge generation provenance

The canonical modified Forge and embedded delta are unchanged from MAYHEM 1.1.

Canonical vanilla Forge:

- size: `3259897607` bytes
- SHA-256: `9E6F14A85B64B4C39683786D50794A714F50DDDCB343B9312A728806AF468D0E`

Canonical 255-stat Forge:

- size: `3260776448` bytes
- SHA-256: `D80B46495C6669DF2BCB2CA674227F8FB646A0BE196502C22770CCB5CB6984AF`

The supported Ubisoft Connect installation uses the same exact vanilla Forge identity as the supported Steam installation. No platform-specific Forge payload was added for 1.2.

The canonical modified Forge was originally generated from 233 validated EnemyRankInfo replacements. The public runtime installer contains only the frozen compact delta, not either complete Forge archive and not the development Forge-builder toolchain.

## 1.2 Forge I/O change

Live HDD testing exposed excessive repeated reads caused by redundant full SHA-256 passes over the same immutable 3.26 GB Forge during one transaction.

The 1.2 final source retains exact identity validation but holds already verified files under transaction-scoped file locks while they are reused. Staged and committed outputs remain independently SHA-256 verified, and the explicit Verify action remains a full independent verification pass.

This changed transaction I/O behavior only. The embedded Forge delta and resulting patched Forge identity did not change.

## Integration validation

Before preparing the 1.2 public review tree:

- Steam representative regression cases reproduced the previously validated patched EXE identities and restored exact vanilla state;
- Ubisoft primary and plus production operations passed exact preimage/signature validation;
- all 12 selectable compositions were conflict-free for both Ubisoft executable families;
- representative Ubisoft dual Apply / Verify / Restore cases passed;
- the full Linear + x6 + Immediate Recycle plus output reproduced the runtime-tested `3275F9...` identity;
- missing Ubisoft sibling behavior failed closed;
- legacy MAYHEM 1.0 restore behavior passed;
- interrupted dual-transaction recovery passed multiple partial-commit scenarios;
- post-I/O-optimization Steam and Ubisoft regression tests preserved the same patched output identities;
- the optimized final installer completed successfully on the user's HDD-based Ubisoft installation and displayed measured installation progress.

## Final 1.2 review artifact

Filename:

`aco_mayhem_installer.exe`

Identity:

- size: `183988213` bytes
- SHA-256: `1869444DF95194510C268D7F25247488F90FFE4B4EED85B40A32198115181927`
- FileVersion: `1.2.0.0`
- ProductVersion: `1.2.0`

A separate publish from a separate source copy reproduced the same byte length and SHA-256.

The final Git commit and 1.2 review tag are recorded after the repository is frozen, because a commit cannot contain its own final commit SHA without changing that SHA.
