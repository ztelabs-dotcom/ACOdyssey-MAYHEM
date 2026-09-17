# Security review notes

Date: 2026-09-17
Scope: MAYHEM 1.2 hardened public installer build

## Supported game baselines

### Steam 1.5.6 / build 17083392

- `ACOdyssey.exe` size: `286453072` bytes
- `ACOdyssey.exe` SHA-256: `AC327DAD2CBBDD72A3FDA8E99CBEAB9D12AF328363E4F09BC5674BDD36B8C483`

### Ubisoft Connect 1.5.6

Primary executable:

- `ACOdyssey.exe` size: `285838672` bytes
- `ACOdyssey.exe` SHA-256: `3CB92F72823DB2C5EC24B77ADCD2325C9E1C61DBDB3E7EEA87151374F49B1A07`

Required sibling executable:

- `ACOdyssey_plus.exe` size: `501398864` bytes
- `ACOdyssey_plus.exe` SHA-256: `422439DA0C0F282B29C6C17F3BDC7B3D81B624B7ACEC2B9C69AED2CA22896560`

### Forge

The supported Steam and Ubisoft Connect targets use the same vanilla Forge identity:

- `DataPC_patch_01.forge` size: `3259897607` bytes
- `DataPC_patch_01.forge` SHA-256: `9E6F14A85B64B4C39683786D50794A714F50DDDCB343B9312A728806AF468D0E`

The installer fails closed on unsupported size/hash/PE/preimage state, missing Ubisoft sibling executable, partial Ubisoft patch state or unknown Forge identity.

## EXE mutation model

Executable mutations are manifest-driven fixed-length byte replacements declared in the embedded `patches.json`.

Before commit the engine validates:

- exact supported target identity;
- operation ranges and overlap rules;
- exact original bytes;
- staging output;
- selected patched bytes;
- target state immediately before replacement.

The executable engine creates a verified vanilla backup, performs work against a staging copy, uses transactional replacement, writes recovery state and rolls back when completion cannot be proven.

The Steam executable retains its previously validated MAYHEM patch topology.

For the supported Ubisoft primary executable, the MAYHEM patch locations use the same relevant file offsets/RVAs and payload topology as the Steam target, while the global executable identity is different and separately allowlisted.

`ACOdyssey_plus.exe` uses its own separately mapped manifest targets. The full Linear + x6 + Selector255 + Fast Travel 1s + Immediate Recycle candidate was validated in live Ubisoft gameplay before 1.2 integration.

## Ubisoft dual-executable coordination

When the exact supported Ubisoft primary `ACOdyssey.exe` is selected, MAYHEM requires the exact sibling `ACOdyssey_plus.exe` in the same directory.

Apply / Verify / Restore treat the Ubisoft installation as one managed set:

1. `ACOdyssey.exe`
2. `ACOdyssey_plus.exe`
3. `DataPC_patch_01.forge`

The two executables must use the same MAYHEM patch selection. A missing sibling, unsupported sibling, partial pair or mismatched pair fails closed.

An installation-level transaction journal coordinates both executable patch engines and the Forge transaction. Interrupted-transaction recovery was tested with multiple partial-commit states, including the state where both executables and the extended Forge were already committed but the final transaction journal had not yet been removed. Recovery returned all managed files to exact vanilla identities.

## Forge mutation model

MAYHEM 1.2 reuses the exact same project-authored Brotli-compressed binary delta as the frozen 1.1 review build:

`Assets\forge-levels255-v1.mfd.br`

- size: `3896382` bytes
- SHA-256: `6771FA43AA0DBEEBE3D9E87448E86EE1C1C18A1096780B4431D49743A34D28CC`

It is used only when Mercenary Level Unlock `Light` or `Linear` is selected.

The installer does not contain Ubisoft's complete Forge archive. It requires the user's exact vanilla `DataPC_patch_01.forge`, validates its size and SHA-256 and reconstructs a staged output locally.

Expected reconstructed output:

- size: `3260776448` bytes
- SHA-256: `D80B46495C6669DF2BCB2CA674227F8FB646A0BE196502C22770CCB5CB6984AF`

The delta header records the expected source/target sizes and SHA-256 values. The application independently validates the embedded delta identity, source Forge identity and staged/committed output identities.

The reconstructed Forge contains the extended 255-record EnemyRankInfo progression data required for combat-stat scaling above level 99. The 233 replacement assets used to generate the canonical output were validated separately during development. The runtime installer applies only the frozen deterministic delta and does not execute external Forge tooling.

## Forge I/O optimization in 1.2

The 1.1 safety model performed several redundant full SHA-256 passes over the same 3.26 GB Forge during a single transaction. This was safe but unnecessarily expensive on HDD installations.

MAYHEM 1.2 keeps the identity checks but avoids redundant re-hashing of a file that has already been verified and is still protected from mutation.

For the install transaction:

- the live vanilla Forge is exact-hash verified;
- the verified source is held under a transaction file lock while reused;
- an existing or newly created vanilla backup is independently exact-hash verified and locked while reused;
- the reconstructed staged Forge is independently exact-hash verified and locked before commit;
- the committed live Forge receives a final exact SHA-256 verification;
- explicit `VERIFY` remains a separate full verification pass.

This reduces repeated disk reads without weakening the validation boundary between hash verification and use.

## Coordinated EXE + Forge transaction

For Level Unlock `Light` or `Linear`:

1. validate exact supported EXE identities and patch preimages;
2. validate the exact vanilla Forge and embedded Forge delta;
3. create/verify immutable vanilla backups;
4. reconstruct and verify a separate staged Forge;
5. apply executable patch transactions;
6. replace the live Forge only after staged verification;
7. verify committed managed state;
8. preserve recovery state if completion cannot be proven.

If Apply fails, the installer attempts to return all managed files to verified original state. Unknown identities fail closed.

For Level Unlock `Off`, the installer requires `DataPC_patch_01.forge` to remain exact vanilla and performs no Forge modification.

## Restore behavior and legacy migration

Restore validates managed state and backup identities before mutation.

For a normal Level Unlock install, Restore returns all managed executable files and `DataPC_patch_01.forge` to exact verified vanilla identities.

The installer also recognizes the legacy MAYHEM 1.0 Level Unlock state where the EXE is patched but the Forge remains exact vanilla and no Forge-managed state exists. In this specific case, Restore restores the EXE and leaves the already-vanilla Forge unchanged.

If Forge state is missing but the live Forge is not the exact vanilla identity, Restore aborts fail-closed.

## Backup/state writes

The UI supports two backup-store modes:

- per-user application data under `%LocalAppData%\Narzelith\MAYHEM`;
- a `Backup` directory beside the installer.

State, recovery and hash-qualified vanilla backups remain associated with the selected store so Verify/Restore can validate exact originals.

## Process behavior

The public source contains no `Process.Start` or `ProcessStartInfo` usage.

It inspects the local process list only to refuse file mutation while relevant Assassin's Creed Odyssey executable processes are active.

The installer does not launch the game, command shells, PowerShell, browsers, helper EXEs or other applications.

## Network behavior

The application contains no runtime download/update path, telemetry or intentional network communication.

Build-time `dotnet restore/publish` may contact the official NuGet source only to obtain Microsoft framework/runtime packs when they are not already cached. This is a build-machine operation, not installer runtime behavior.

## Registry / shell / native interop

Public application source contains no custom:

- registry access;
- shell invocation;
- P/Invoke/native interop declarations;
- dynamic library loading.

## Single-file runtime extraction

The application is self-contained and published with `IncludeNativeLibrariesForSelfExtract=true` because the official .NET single-file host must materialize Microsoft native runtime components at launch.

Single-file compression is disabled.

The application does not use this mechanism to hide or launch a third-party patcher/helper executable.

## Embedded resources

Application/project resources compiled into the executable:

- `patches.json`;
- `forge-levels255-v1.mfd.br`;
- installer artwork;
- installer audio/music;
- application icon.

The Forge delta and visual/audio assets are byte-identical to the frozen 1.1 review inputs. The artwork, audio/music and icon assets are original works owned by Narzelith; see `ASSET_RIGHTS.md`.

No Ubisoft executable or complete proprietary game package is embedded.

## Third-party code/dependencies

The installer project has no external application-package references.

The self-contained executable includes Microsoft .NET runtime components generated by the official SDK publish process.

No UPKUtils, AnvilToolkit, Forge-builder or other third-party helper executable/DLL is present in the runtime build.

## Privilege model

The project contains no custom elevation/bootstrapper code and does not self-elevate. File writes execute with the permissions of the user who launched the installer.
