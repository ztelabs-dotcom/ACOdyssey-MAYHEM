# MAYHEM - Assassin's Creed Odyssey Installer

Source package for the MAYHEM Windows installer and binary patch manager.

## Supported game build

- Assassin's Creed Odyssey - Steam
- game version: 1.5.6
- Steam build: 17083392
- canonical `ACOdyssey.exe` size: `286453072` bytes
- canonical `ACOdyssey.exe` SHA-256: `AC327DAD2CBBDD72A3FDA8E99CBEAB9D12AF328363E4F09BC5674BDD36B8C483`

The installer fails closed if the target executable does not match the supported PE/build/hash/size/preimage requirements.

## What the installer does

The application applies a declared set of fixed-length local binary patches to the user's own supported `ACOdyssey.exe`. The patch definitions are stored in `src/ACOdysseyUMM/patches.json` and compiled into the application as a resource.

The patch engine:

- validates the exact supported game executable;
- validates original bytes before every patch operation;
- creates and SHA-256 validates a vanilla backup;
- patches an isolated staging copy;
- verifies patched bytes and full target state before commit;
- replaces the target transactionally;
- records a recovery journal;
- restores the original executable on request;
- rolls back automatically if a write/commit verification fails;
- refuses Apply/Restore while `ACOdyssey.exe` is running.

The installer does not contain or redistribute Ubisoft's `ACOdyssey.exe` or other complete proprietary game binaries.

## Release architecture

- C# / WinForms
- target framework: `net8.0-windows`
- review build SDK: `.NET SDK 9.0.308`
- runtime: `win-x64`
- self-contained
- single-file
- single-file compression disabled
- Microsoft native runtime libraries included for standard .NET single-file extraction
- no external NuGet application packages
- no third-party helper EXE or DLL shipped by the hardened public build

Embedded project resources:

- `patches.json`
- installer artwork
- installer audio
- application icon

## Security behavior

The hardened public build has no runtime networking, updater, telemetry, registry access, shell invocation, process launching, P/Invoke/native interop, or third-party executable extraction. It only inspects the process list for `ACOdyssey.exe` so it can refuse writes while the game is running.

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
