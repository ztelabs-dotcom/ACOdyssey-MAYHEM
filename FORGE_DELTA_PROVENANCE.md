# MAYHEM Forge delta provenance

Date: 2026-09-17

## Purpose

The Forge stat-extension payload used by MAYHEM 1.2 is byte-identical to the frozen MAYHEM 1.1 review payload.

MAYHEM 1.0 patched the executable-level Mercenary hierarchy/selector logic but did not install extended EnemyRankInfo progression data. With the vanilla 99-record RankInfo vectors still present, the game's existing level-based lookup clamps any level above 99 to the final available record.

MAYHEM 1.1 added the missing data layer through a small deterministic binary delta rather than redistributing Ubisoft's complete Forge archive. MAYHEM 1.2 reuses that exact delta unchanged.

## Exact source and target identities

Vanilla input expected from the user's own supported Steam or Ubisoft Connect installation:

- file: `DataPC_patch_01.forge`
- size: `3259897607` bytes
- SHA-256: `9E6F14A85B64B4C39683786D50794A714F50DDDCB343B9312A728806AF468D0E`

The supported Steam and Ubisoft Connect installations use this same exact Forge identity.

Canonical MAYHEM extended-stat output:

- size: `3260776448` bytes
- SHA-256: `D80B46495C6669DF2BCB2CA674227F8FB646A0BE196502C22770CCB5CB6984AF`

Embedded delta:

- file: `src/ACOdysseyUMM/Assets/forge-levels255-v1.mfd.br`
- size: `3896382` bytes
- SHA-256: `6771FA43AA0DBEEBE3D9E87448E86EE1C1C18A1096780B4431D49743A34D28CC`

This delta file is byte-identical between the 1.1 and 1.2 Nexus review sources.

## Data-generation validation

The canonical target Forge was built from 233 EnemyRankInfo replacement assets.

Validation summary:

- generation: 233/233 PASS
- resource mutation / serialization round trip: 233/233 PASS
- relevant Mercenary RankInfo profiles: 24

For the principal `ACD Merc - Epic Overall` profile, the final 255-record payload contains distinct increasing records above 99. Example audited values include:

```text
Level 99 : f28=5772860     f30=155507      i58=4030
Level 115: f28=8423998     f30=223992.7188 i58=6593
Level 226: f28=46310220    f30=1161296.375 i58=60715
Level 255: f28=62797352    f30=1558331.375 i58=90281
```

The public runtime installer does not generate these progression assets dynamically. It uses the already validated frozen delta and verifies the complete reconstructed Forge identity before commit.

## Delta format

The uncompressed delta is a simple project-specific deterministic stream:

- magic: `MYHFD11\0`
- format version
- source size
- target size
- source SHA-256
- target SHA-256
- command count
- sequential COPY/LITERAL commands

The stored resource is Brotli-compressed. `ForgeLevel255Manager.cs` validates the compressed resource identity, parses the header, validates source/target identities and applies COPY/LITERAL commands into a newly created staging file.

No shell, external patcher, third-party Forge utility or dynamically downloaded component is used at runtime.

## 1.2 transaction I/O behavior

MAYHEM 1.2 changes how already verified Forge files are reused inside one installer transaction, but does not change the delta format, source identity or target identity.

A verified source/backup/stage file can be held under a transaction file lock while reused so that the same 3.26 GB file does not need to be fully re-hashed repeatedly. The staged target and committed live target are still exact SHA-256 verified, and explicit Verify remains a separate full verification pass.

## Independent reconstruction proof

The frozen delta was independently applied to the canonical vanilla Forge during development. The reconstructed file matched the canonical target byte-for-byte by SHA-256:

`D80B46495C6669DF2BCB2CA674227F8FB646A0BE196502C22770CCB5CB6984AF`

Installer integration testing confirmed that Light/Linear configurations produce this exact target Forge, while Level Unlock Off retains the exact vanilla Forge. Restore returns managed test cases to exact original file identities.

## Redistribution boundary

Neither the complete vanilla Forge nor the complete modified Forge is present in this repository or embedded in the installer.

The delta is only meaningful against the one exact supported source identity and fails closed for any other input.
