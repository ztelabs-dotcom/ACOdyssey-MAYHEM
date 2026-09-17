using System.Text.Json.Serialization;

namespace ACOdysseyUMM;

internal sealed class PatchManifest
{
    public int SchemaVersion { get; init; } = 1;
    public List<SupportedBuild> Builds { get; init; } = [];
    public List<PatchDefinition> Patches { get; init; } = [];
}

internal sealed class SupportedBuild
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Sha256 { get; init; }
    public long Size { get; init; }
    public uint PeTimestamp { get; init; }
    public string? Notes { get; init; }
}

internal sealed class PatchDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = string.Empty;
    public List<PatchTarget> Targets { get; init; } = [];
}

internal sealed class PatchTarget
{
    public required string BuildId { get; init; }
    public List<PatchOperation> Operations { get; init; } = [];
}

internal sealed class PatchOperation
{
    public long FileOffset { get; init; }
    public string? Rva { get; init; }
    public required string OriginalHex { get; init; }
    public required string PatchedHex { get; init; }
    public long? SignatureOffset { get; init; }
    public string? SignatureHex { get; init; }
    public string? Note { get; init; }
}

internal sealed class BackupMetadata
{
    public int SchemaVersion { get; init; } = 1;
    public required string OriginalAbsolutePath { get; init; }
    public long Size { get; init; }
    public required string Sha256 { get; init; }
    public required string BackupAbsolutePath { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public required string OperationId { get; init; }
    public required string TransactionId { get; init; }
}

internal sealed class PatchState
{
    public required string BuildId { get; init; }
    public required string GameExePath { get; init; }
    public required string OriginalSha256 { get; init; }
    public required string BackupPath { get; init; }
    public required string PatchedSha256 { get; init; }
    public List<string> AppliedPatchIds { get; init; } = [];
    public DateTimeOffset AppliedAtUtc { get; init; }
}

internal sealed class PatchTransaction
{
    public required string Operation { get; init; }
    public required string BuildId { get; init; }
    public required string GameExePath { get; init; }
    public required string OriginalSha256 { get; init; }
    public required string PatchedSha256 { get; init; }
    public required string BackupPath { get; init; }
    public required string RollbackPath { get; init; }
    public List<string> AppliedPatchIds { get; init; } = [];
    public DateTimeOffset StartedAtUtc { get; init; }
}

internal sealed class UbisoftDualTransaction
{
    public int SchemaVersion { get; init; } = 1;
    public required string Operation { get; init; }
    public required string Phase { get; set; }
    public required string PrimaryExePath { get; init; }
    public required string PlusExePath { get; init; }
    public List<string> AppliedPatchIds { get; init; } = [];
    public DateTimeOffset StartedAtUtc { get; init; }
}

internal sealed record TargetAnalysis(
    string Path,
    string Sha256,
    long Size,
    uint PeTimestamp,
    SupportedBuild? Build,
    bool MatchesSavedPatchedState,
    bool HasStateConflict,
    string Status);

internal sealed record PatchPreflightResult(
    string GameExePath,
    string BuildId,
    IReadOnlyList<string> PatchIds,
    int OperationCount,
    string Sha256);

internal sealed record PatchVerifyResult(
    string GameExePath,
    string Status,
    bool IsValid,
    bool IsPatched,
    string? BuildId,
    string Sha256,
    int VerifiedOperationCount,
    string? BackupPath);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PatchManifest))]
[JsonSerializable(typeof(PatchState))]
[JsonSerializable(typeof(PatchTransaction))]
[JsonSerializable(typeof(UbisoftDualTransaction))]
[JsonSerializable(typeof(BackupMetadata))]
internal partial class AppJsonContext : JsonSerializerContext;
