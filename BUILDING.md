# Building the Nexus review artifact

## Required environment

- Windows x64
- Git
- PowerShell 5.1 or PowerShell 7+
- .NET SDK `9.0.308`

`global.json` pins SDK `9.0.308` with roll-forward disabled.

The project targets `net8.0-windows` and publishes a `win-x64` self-contained application. There are no external NuGet application-package dependencies. On a machine without the required Microsoft targeting/runtime packs cached, `dotnet restore/publish` may retrieve Microsoft framework/runtime packs from NuGet.

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

The `IncludeSourceRevisionInInformationalVersion=false` switch is intentional. It prevents the .NET SDK from injecting the current Git commit into `AssemblyInformationalVersion`, which would alter the single-file binary merely because the same source was built inside a Git repository.

## Expected output

```text
artifacts\aco_mayhem_installer.exe
```

Expected size:

```text
179970037 bytes
```

Expected SHA-256:

```text
E7D5568DAE4D231F53FB5B116E99AA1CC972B077EE8DA3936AD541DA39FBB6F9
```

`build-review.ps1` verifies both values automatically and prints:

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

Do not copy old `bin`, `obj`, or publish output into the clone.

## Application icon provenance

The compiled `Assets\Mayhem.ico` is part of the frozen build input and has SHA-256:

```text
14E23A8DECCCF8F63F93CAE9C4720ED2492F28AB17785C317A620375AC6A04C3
```

The source PNG and historical deterministic icon-generation script are retained for provenance. The canonical review build uses the frozen ICO directly so image-library implementation differences cannot change the reviewed PE.
