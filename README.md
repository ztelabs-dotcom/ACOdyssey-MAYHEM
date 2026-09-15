# MAYHEM - Assassin's Creed Odyssey Installer

Source package for the MAYHEM Windows installer and binary patch manager.

## Supported game build

- Assassin's Creed Odyssey - Steam
- game version: 1.5.6
- Steam build: 17083392
- canonical `ACOdyssey.exe` size: `286453072` bytes
- canonical `ACOdyssey.exe` SHA-256: `AC327DAD2CBBDD72A3FDA8E99CBEAB9D12AF328363E4F09BC5674BDD36B8C483`
- canonical `DataPC_patch_01.forge` size: `3259897607` bytes
- canonical `DataPC_patch_01.forge` SHA-256: `9E6F14A85B64B4C39683786D50794A714F50DDDCB343B9312A728806AF468D0E`

The installer fails closed if the target executable or required Forge input does not match the supported identity/preimage requirements.

## What the installer does

The application applies a declared set of fixed-length local binary patches to the user's own supported `ACOdyssey.exe`. The patch definitions are stored in `src/ACOdysseyUMM/patches.json` and compiled into the application as a resource.

MAYHEM 1.1 also adds the missing extended stat-data layer for Mercenary Level Unlock. When `Light` or `Linear` is selected, the installer uses an embedded deterministic delta to reconstruct a verified 255-record `DataPC_patch_01.forge` from the user's exact vanilla archive. Level Unlock `Off` requires the Forge to remain exact vanilla.

The installer:

- validates the exact supported game executable;
- validates original EXE bytes before every patch operation;
- validates the exact vanilla Forge before Forge modification;
- validates the embedded Forge delta before use;
- creates SHA-256 verified vanilla backups;
- patches/reconstructs isolated staging files;
- verifies patched bytes and complete staged Forge identity before commit;
- coordinates EXE + Forge installation transactionally;
- records recovery state;
- restores exact vanilla files on request;
- rolls back when write/commit verification cannot be proven;
- recognizes the legacy MAYHEM 1.0 Level Unlock state where only the EXE was patched;
- refuses Apply/Restore while `ACOdyssey.exe` is running.

The installer does not contain or redistribute Ubisoft's `ACOdyssey.exe`, complete `DataPC_patch_01.forge`, or other complete proprietary game binaries.

## Release architecture

- C# / WinForms
- version: `1.1.0`
- target framework: `net8.0-windows`
- review build SDK: `.NET SDK 9.0.308`
- runtime: `win-x64`
- self-contained
- single-file
- single-file compression disabled
- Microsoft native runtime libraries included for standard .NET single-file extraction
- no external NuGet application packages
- no third-party helper EXE or DLL shipped by the public build

Embedded project resources:

- `patches.json`
- `forge-levels255-v1.mfd.br`
- installer artwork
- installer audio
- application icon

The artwork, audio/music and icon assets are original works owned by Narzelith. Their presence in this public review repository does not grant redistribution or reuse rights. See `ASSET_RIGHTS.md`.

## Security behavior

The public build has no runtime networking, updater, telemetry, registry access, shell invocation, process launching, P/Invoke/native interop, or third-party executable extraction. It only inspects the process list for `ACOdyssey.exe` so it can refuse writes while the game is running.

See `SECURITY_REVIEW.md` for the complete review-oriented behavior description.

## Build

Run from a Windows PowerShell terminal at repository root:

```powershell
.\build-review.ps1
```

The script requires the exact SDK pinned by `global.json`, publishes the reviewed single-file EXE and fails if byte length or SHA-256 differ from the review artifact.

Detailed instructions: `BUILDING.md`.

Artifact identity: `NEXUS_REVIEW_BUILD.md`.

Source/artifact provenance: `SOURCE_PROVENANCE.md`.

Forge delta provenance: `FORGE_DELTA_PROVENANCE.md`.
