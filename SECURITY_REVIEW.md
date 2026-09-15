# Security review notes

Date: 2026-09-15
Scope: MAYHEM 1.1 hardened public installer build

## Supported game baseline

- Assassin's Creed Odyssey Steam 1.5.6 / build 17083392
- `ACOdyssey.exe` size: `286453072` bytes
- `ACOdyssey.exe` SHA-256: `AC327DAD2CBBDD72A3FDA8E99CBEAB9D12AF328363E4F09BC5674BDD36B8C483`
- `DataPC_patch_01.forge` vanilla size: `3259897607` bytes
- `DataPC_patch_01.forge` vanilla SHA-256: `9E6F14A85B64B4C39683786D50794A714F50DDDCB343B9312A728806AF468D0E`

The installer fails closed on unsupported size/hash/PE/preimage state or unknown Forge identity.

## EXE mutation model

Executable mutations remain manifest-driven fixed-length byte replacements declared in the embedded `patches.json`.

Before commit the engine validates:

- exact supported target identity;
- operation ranges and overlap rules;
- exact original bytes;
- staging output;
- selected patched bytes;
- target state immediately before replacement.

The executable engine creates a verified vanilla backup, performs work against a staging copy, uses transactional replacement, writes recovery state and rolls back when completion cannot be proven.

## Forge mutation model added in 1.1

MAYHEM 1.1 contains one project-authored Brotli-compressed binary delta as an embedded resource:

`Assets\forge-levels255-v1.mfd.br`

- size: `3896382` bytes
- SHA-256: `6771FA43AA0DBEEBE3D9E87448E86EE1C1C18A1096780B4431D49743A34D28CC`

It is used only when Mercenary Level Unlock `Light` or `Linear` is selected.

The installer does not contain Ubisoft's complete Forge archive. Instead it requires the user's exact vanilla `DataPC_patch_01.forge`, validates its size and SHA-256, and reconstructs a staged output locally.

Expected reconstructed output:

- size: `3260776448` bytes
- SHA-256: `D80B46495C6669DF2BCB2CA674227F8FB646A0BE196502C22770CCB5CB6984AF`

The delta header itself records the expected source/target sizes and SHA-256 values. The application independently validates the embedded delta resource identity, source Forge identity and complete staged output identity before commit.

The reconstructed Forge contains the extended 255-record EnemyRankInfo progression data required for combat-stat scaling above level 99. The 233 replacement assets used to generate the canonical output were validated separately during development; the runtime installer applies only the frozen deterministic delta and does not execute external Forge tooling.

## Coordinated EXE + Forge transaction

For `Light` or `Linear`:

1. validate exact vanilla EXE/patch preimages and exact vanilla Forge;
2. validate the embedded Forge delta;
3. create/verify immutable vanilla backups;
4. reconstruct and verify a separate staged Forge;
5. apply the EXE patch transaction;
6. replace the live Forge only after staged Forge verification;
7. verify the final EXE + Forge state;
8. preserve recovery state if completion cannot be proven.

If Apply fails, the installer attempts to return both files to their verified original state. Unknown identities fail closed.

For Level Unlock `Off`, the installer requires `DataPC_patch_01.forge` to remain exact vanilla and performs no Forge modification.

## Restore behavior and MAYHEM 1.0 migration

Restore validates the managed state and backup identities before mutation.

For a normal 1.1 Level Unlock install, Restore returns both `ACOdyssey.exe` and `DataPC_patch_01.forge` to the exact verified vanilla identities.

The 1.1 installer also recognizes the legacy MAYHEM 1.0 Level Unlock state: an EXE patch state may exist while the Forge remains exact vanilla and there is no Forge-managed state. In this specific case, Restore restores the EXE and leaves the already-vanilla Forge unchanged. It never invents or substitutes a missing Forge backup.

If Forge state is missing but the live Forge is not the exact vanilla identity, Restore aborts fail-closed.

## Backup/state writes

The UI supports two backup-store modes:

- per-user application data under `%LocalAppData%\Narzelith\MAYHEM`;
- a `Backup` directory beside the installer.

State, recovery and hash-qualified vanilla backups remain associated with the selected store so Verify/Restore can validate the exact originals.

## Process behavior

The public source contains no `Process.Start` or `ProcessStartInfo` usage.

It calls `Process.GetProcessesByName("ACOdyssey")` only to detect whether the game is running. Apply and Restore are refused while the game process is active.

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

The artwork, audio/music and icon assets are original works owned by Narzelith; see `ASSET_RIGHTS.md`.

No Ubisoft executable or complete proprietary game package is embedded.

## Third-party code/dependencies

The installer project has no external application-package references.

The self-contained executable includes Microsoft .NET runtime components generated by the official SDK publish process.

No UPKUtils, AnvilToolkit, Forge-builder or other third-party helper executable/DLL is present in the runtime build.

## Privilege model

The project contains no custom elevation/bootstrapper code and does not self-elevate. File writes execute with the permissions of the user who launched the installer.
