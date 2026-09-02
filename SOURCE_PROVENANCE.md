# Source and artifact provenance

Date: 2026-09-02

## Existing release control artifact

Path at audit time:

`F:\Workspace\MODDING\ACO\Release\installer\MAHYEM - Steam-1.5.6-build\aco_mayhem_installer.exe`

Identity:

- size: `89825327` bytes
- SHA-256: `9EEF79BFE57AA657FE8070763867E74FD888214558BCB392961A8984ADB219AD`

The existing release executable was copied to the isolated review tree and marked read-only before reconstruction work.

## Control-source reconstruction proof

The current installer source was combined with the preserved release assets from the August 31 backup and the preserved deterministic icon-generation source/script.

The reconstructed build used:

```powershell
dotnet publish .\ACOdysseyUMM.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none `
  -p:DebugSymbols=false `
  -p:IncludeSourceRevisionInInformationalVersion=false
```

Result:

- reconstructed size: `89825327` bytes
- reconstructed SHA-256: `9EEF79BFE57AA657FE8070763867E74FD888214558BCB392961A8984ADB219AD`
- `MATCH_CONTROL=True`

This proves the recovered source/build inputs reproduce the existing release artifact byte-for-byte.

## Hardened public-review delta

A new public-review candidate was then derived from that byte-proven source.

Intentional changes only:

1. Single-file compression disabled.
2. Meaningful PE product/company/version metadata added.
3. Assembly/output name changed to `aco_mayhem_installer`.
4. `NpcLevels255Coordinator.cs` omitted from the public GUI source/build because it is unreachable from `MainForm.cs`, `Program.cs` and the release `patches.json` and would otherwise leave dormant Forge-builder `Process.Start` code in the public executable.

No patch manifest, gameplay patch bytes, GUI behavior source, patch-engine source, artwork, audio or application icon content changed.

Automated common-file hash comparison result:

`UNCHANGED_ALL_COMMON_SOURCE_AND_RESOURCE_FILES=TRUE`

Key frozen hashes:

- `patches.json`: `CD3360C83172AD3B48F50010606F2F905FE4B6C38F594A73D414960615387CE9`
- `MainForm.cs`: `0DE178ED492D965564A7278C5D75A19209189E8D6C05626932B3A7D9C3BB800B`
- `PatchEngine.cs`: `970C41073D7244AFF31AF29E4319CF019C1CCCBD862B564104359CCA85334023`
- `MercenaryInstallSelection.cs`: `5E29F9394B7D7E8C3F5AEE3495A7A7038D89ADABD0CCFC94C4D26C131D58F463`
- `NightmareScalingPolicy.cs`: `93B7A805A0A6FA1A2F6101F40A6E4C33001155762A7376C94E4D82DBEAEBD942`
- `InstallerArt.png`: `8F4611FCF34BF92323C0DEA9AE55BE693EFC391830C1C8A7683B92FAE9DC625F`
- `InstallerAudio.wav`: `3170BCD81EEB6DF9A54EFAC13E033FF67E84D419B9C495085474E78EDC6D7515`
- `Mayhem.ico`: `14E23A8DECCCF8F63F93CAE9C4720ED2492F28AB17785C317A620375AC6A04C3`

## Hardened review artifact

- filename: `aco_mayhem_installer.exe`
- size: `179970037` bytes
- SHA-256: `E7D5568DAE4D231F53FB5B116E99AA1CC972B077EE8DA3936AD541DA39FBB6F9`

The final Git fresh-clone reproduction result and exact commit/tag are recorded outside the repository after the repository is frozen, because a Git commit cannot contain its own final commit hash without changing that hash.
