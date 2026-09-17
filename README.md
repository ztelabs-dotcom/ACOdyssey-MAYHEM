# MAYHEM - Assassin's Creed Odyssey Installer

Source package for the MAYHEM Windows installer and binary patch manager.

## Supported game builds

MAYHEM 1.2 supports exact Assassin's Creed Odyssey 1.5.6 executable identities on Steam and Ubisoft Connect.

### Steam

- file: `ACOdyssey.exe`
- Steam build: `17083392`
- size: `286453072` bytes
- SHA-256: `AC327DAD2CBBDD72A3FDA8E99CBEAB9D12AF328363E4F09BC5674BDD36B8C483`

### Ubisoft Connect primary

- file: `ACOdyssey.exe`
- size: `285838672` bytes
- SHA-256: `3CB92F72823DB2C5EC24B77ADCD2325C9E1C61DBDB3E7EEA87151374F49B1A07`

### Ubisoft Connect sibling

- file: `ACOdyssey_plus.exe`
- size: `501398864` bytes
- SHA-256: `422439DA0C0F282B29C6C17F3BDC7B3D81B624B7ACEC2B9C69AED2CA22896560`

For an exact supported Ubisoft Connect installation, the installer requires both sibling executables and manages them together. This avoids relying on the user to know which Ubisoft executable the launcher may ultimately use.

### Forge input

The supported Steam and Ubisoft Connect installations use the same exact vanilla Forge identity:

- file: `DataPC_patch_01.forge`
- size: `3259897607` bytes
- SHA-256: `9E6F14A85B64B4C39683786D50794A714F50DDDCB343B9312A728806AF468D0E`

The installer fails closed if a required executable, Forge input, patch preimage, saved state or backup identity does not match the supported values.

## What the installer does

The application applies declared fixed-length local binary patches to the user's own supported game executable files. Patch definitions are stored in `src/ACOdysseyUMM/patches.json` and compiled into the application as a resource.

MAYHEM 1.2 retains the deterministic Forge stat-extension layer introduced in 1.1. When Mercenary Level Unlock `Light` or `Linear` is selected, the installer uses the same embedded deterministic delta to reconstruct a verified 255-record `DataPC_patch_01.forge` from the user's exact vanilla archive. Level Unlock `Off` requires the Forge to remain exact vanilla.

On Steam, Apply / Verify / Restore manage the supported `ACOdyssey.exe` and Forge state.

On Ubisoft Connect, Apply / Verify / Restore coordinate all three managed files:

1. `ACOdyssey.exe`
2. `ACOdyssey_plus.exe`
3. `DataPC_patch_01.forge`

The installer:

- validates exact supported executable identities and patch preimages;
- validates the exact vanilla Forge before Forge modification;
- validates the embedded Forge delta before use;
- creates SHA-256 verified vanilla backups;
- patches/reconstructs isolated staging files;
- verifies staged output before commit;
- coordinates multi-file installation transactionally;
- records recovery state;
- restores exact vanilla files on request;
- rolls back when completion cannot be proven;
- recognizes the legacy MAYHEM 1.0 Level Unlock state where only the EXE was patched;
- refuses mutation while the relevant Odyssey executable is running.

MAYHEM 1.2 also reduces redundant full-file reads during a single Forge transaction. A file that has already been hash-verified is kept under an exclusive/immutable transaction lock while it is reused, instead of being re-hashed repeatedly. Staged and committed outputs still receive exact SHA-256 verification. The explicit `VERIFY` action remains a full independent verification pass.

The installer does not contain or redistribute Ubisoft's complete executables, complete `DataPC_patch_01.forge`, or other complete proprietary game binaries.

## Release architecture

- C# / WinForms
- version: `1.2.0`
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

The Forge delta and all installer artwork/audio/icon assets are byte-identical to the frozen 1.1 review inputs. The artwork, audio/music and icon assets are original works owned by Narzelith. Their presence in this public review repository does not grant redistribution or reuse rights. See `ASSET_RIGHTS.md`.

## Security behavior

The public build has no runtime networking, updater, telemetry, registry access, shell invocation, process launching, P/Invoke/native interop or third-party executable extraction. It only inspects the process list so writes can be refused while the game is active.

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
