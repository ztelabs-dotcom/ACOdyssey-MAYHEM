# Building the Nexus review artifact

## Required environment

- Windows x64
- Git
- PowerShell 5.1 or PowerShell 7+
- .NET SDK `9.0.308`

`global.json` pins SDK `9.0.308` with roll-forward disabled.

The project targets `net8.0-windows` and publishes a `win-x64` self-contained application. There are no external NuGet application-package dependencies. `NuGet.Config` clears inherited package sources and uses only the official `https://api.nuget.org/v3/index.json` source for Microsoft framework/runtime packs when they are not already cached.

## Canonical build

From repository root:

```powershell
.\build-review.ps1
```

Equivalent publish command:

```powershell
dotnet publish .\src\ACOdysseyUMM\ACOdysseyUMM.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=false `
  -p:DebugType=none `
  -p:DebugSymbols=false `
  -p:IncludeSourceRevisionInInformationalVersion=false `
  -o .\artifacts
```

`IncludeSourceRevisionInInformationalVersion=false` prevents the SDK from injecting the current Git commit into the assembly metadata, so the same frozen source produces the same review artifact regardless of clone revision metadata.

## Expected output

```text
artifacts\aco_mayhem_installer.exe
```

Expected size:

```text
183910389 bytes
```

Expected SHA-256:

```text
AD13D7543CC503C25B3AAA200E03178B58F27CAFAEF6B1A327834CFB8D78A739
```

`build-review.ps1` verifies both values and prints:

```text
MATCH_REVIEW_ARTIFACT=True
```

only when both match.

## Clean-clone verification procedure

1. Clone the exact Nexus review tag into a new empty directory.
2. Confirm `dotnet --version` resolves to `9.0.308`.
3. Run only `build-review.ps1`.
4. Confirm the script prints the expected size and SHA-256.
5. Confirm `MATCH_REVIEW_ARTIFACT=True`.

Do not copy old `bin`, `obj`, publish output, or a prebuilt installer into the clone.

## MAYHEM 1.1 Forge payload

MAYHEM 1.1 adds one embedded project-authored binary delta:

```text
src\ACOdysseyUMM\Assets\forge-levels255-v1.mfd.br
```

Identity:

- size: `3896382` bytes
- SHA-256: `6771FA43AA0DBEEBE3D9E87448E86EE1C1C18A1096780B4431D49743A34D28CC`

The installer applies this delta only when Mercenary Level Unlock `Light` or `Linear` is selected. It reconstructs the supported modified `DataPC_patch_01.forge` from the user's exact vanilla archive. The complete Ubisoft Forge archive is not embedded or redistributed.

Expected source Forge:

- size: `3259897607` bytes
- SHA-256: `9E6F14A85B64B4C39683786D50794A714F50DDDCB343B9312A728806AF468D0E`

Expected reconstructed Forge:

- size: `3260776448` bytes
- SHA-256: `D80B46495C6669DF2BCB2CA674227F8FB646A0BE196502C22770CCB5CB6984AF`

The delta itself contains source/target size and SHA-256 identities. The runtime also independently validates the embedded delta hash and both Forge identities before commit.

## Application icon provenance

The compiled `Assets\Mayhem.ico` remains part of the frozen build input. The source PNG and historical deterministic icon-generation script are retained for provenance. The canonical review build uses the frozen ICO directly so image-library implementation differences cannot change the reviewed PE.
