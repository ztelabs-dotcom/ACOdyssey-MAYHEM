$ErrorActionPreference = 'Stop'

$ExpectedSdk = '9.0.308'
$ExpectedSize = 183988213
$ExpectedSha256 = '1869444DF95194510C268D7F25247488F90FFE4B4EED85B40A32198115181927'

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $RepoRoot 'src\ACOdysseyUMM\ACOdysseyUMM.csproj'
$Artifacts = Join-Path $RepoRoot 'artifacts'
$OutputExe = Join-Path $Artifacts 'aco_mayhem_installer.exe'

$sdk = (& dotnet --version).Trim()
if ($sdk -ne $ExpectedSdk) {
    throw "Exact .NET SDK $ExpectedSdk is required; active SDK is $sdk."
}

if (-not (Test-Path $Artifacts)) {
    New-Item -ItemType Directory -Path $Artifacts | Out-Null
}

& dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:IncludeSourceRevisionInInformationalVersion=false `
    -o $Artifacts

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$item = Get-Item $OutputExe
$hash = (Get-FileHash $OutputExe -Algorithm SHA256).Hash

Write-Output "EXE=$OutputExe"
Write-Output "BYTES=$($item.Length)"
Write-Output "SHA256=$hash"

if ($item.Length -ne $ExpectedSize) {
    throw "Byte length mismatch. Expected $ExpectedSize, got $($item.Length)."
}
if ($hash -ne $ExpectedSha256) {
    throw "SHA-256 mismatch. Expected $ExpectedSha256, got $hash."
}

Write-Output 'MATCH_REVIEW_ARTIFACT=True'
