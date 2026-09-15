using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ACOdysseyUMM;

internal sealed class ForgeLevel255Manager
{
    public const string ForgeFileName = "DataPC_patch_01.forge";
    public const long VanillaForgeSize = 3_259_897_607;
    public const long PatchedForgeSize = 3_260_776_448;
    public const string VanillaForgeSha256 = "9E6F14A85B64B4C39683786D50794A714F50DDDCB343B9312A728806AF468D0E";
    public const string PatchedForgeSha256 = "D80B46495C6669DF2BCB2CA674227F8FB646A0BE196502C22770CCB5CB6984AF";
    public const long EmbeddedDeltaSize = 3_896_382;
    public const string EmbeddedDeltaSha256 = "6771FA43AA0DBEEBE3D9E87448E86EE1C1C18A1096780B4431D49743A34D28CC";
    public const string DeltaResourceName = "ACOdysseyUMM.Resources.forge-levels255-v1.mfd.br";

    private const int StateSchemaVersion = 1;
    private const int TransactionSchemaVersion = 1;
    private const long DiskSafetyMargin = 256L * 1024 * 1024;

    private readonly string _storageRoot;
    private readonly string _backupRoot;
    private readonly string _statePath;
    private readonly string _transactionPath;
    private readonly string _engineStatePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ForgeLevel255Manager(string storageRoot, string? backupRoot = null)
    {
        _storageRoot = Path.GetFullPath(storageRoot);
        _backupRoot = Path.GetFullPath(backupRoot ?? Path.Combine(_storageRoot, "Backups"));
        _statePath = Path.Combine(_storageRoot, "mayhem-forge-state.json");
        _transactionPath = Path.Combine(_storageRoot, "mayhem-forge-transaction.json");
        _engineStatePath = Path.Combine(_storageRoot, "umm-state.json");
    }

    public static bool RequiresExtendedStats(IReadOnlyCollection<string> patchIds) =>
        patchIds.Contains(MercenaryPatchSelectionResolver.LevelLinearPatchId, StringComparer.Ordinal) ||
        patchIds.Contains(MercenaryPatchSelectionResolver.LevelLightPatchId, StringComparer.Ordinal);

    public async Task RecoverInterruptedAsync(string exePath, PatchEngine engine, CancellationToken cancellationToken = default)
    {
        var tx = LoadTransaction();
        if (tx is null)
            return;

        var fullExePath = Path.GetFullPath(exePath);
        ValidateTransactionIdentity(tx, fullExePath);
        var forgePath = ResolveForgePath(fullExePath);
        if (!PathsEqual(tx.ForgePath, forgePath))
            throw new InvalidDataException("Forge recovery transaction belongs to a different game path.");

        var forgeIdentity = await GetIdentityAsync(forgePath, cancellationToken);
        var engineState = LoadEngineState();
        var matchingEngineState = engineState is not null && PathsEqual(engineState.GameExePath, fullExePath)
            ? engineState
            : null;

        if (string.Equals(tx.Operation, "apply", StringComparison.Ordinal))
        {
            if (IsPatched(forgeIdentity) && matchingEngineState is not null && RequiresExtendedStats(matchingEngineState.AppliedPatchIds))
            {
                await ValidateVanillaBackupAsync(tx.BackupPath, cancellationToken);
                if (LoadState() is null)
                    await SaveStateAsync(CreateStateFromTransaction(tx), cancellationToken);
                DeleteExactFileIfExists(tx.StagePath);
                DeleteExactFileIfExists(_transactionPath);
                return;
            }

            if (IsVanilla(forgeIdentity))
            {
                if (matchingEngineState is not null)
                    await engine.RestoreAsync(fullExePath, cancellationToken);
                DeleteExactFileIfExists(tx.StagePath);
                DeleteExactFileIfExists(_transactionPath);
                return;
            }

            await ValidateVanillaBackupAsync(tx.BackupPath, cancellationToken);
            await RestoreForgeFromBackupAsync(forgePath, tx.BackupPath, cancellationToken);
            if (matchingEngineState is not null)
                await engine.RestoreAsync(fullExePath, cancellationToken);
            DeleteExactFileIfExists(tx.StagePath);
            DeleteExactFileIfExists(_statePath);
            DeleteExactFileIfExists(_transactionPath);
            return;
        }

        if (string.Equals(tx.Operation, "restore", StringComparison.Ordinal))
        {
            if (matchingEngineState is not null)
                await engine.RestoreAsync(fullExePath, cancellationToken);

            forgeIdentity = await GetIdentityAsync(forgePath, cancellationToken);
            if (!IsVanilla(forgeIdentity))
            {
                await ValidateVanillaBackupAsync(tx.BackupPath, cancellationToken);
                await RestoreForgeFromBackupAsync(forgePath, tx.BackupPath, cancellationToken);
            }

            DeleteExactFileIfExists(tx.StagePath);
            DeleteExactFileIfExists(_statePath);
            DeleteExactFileIfExists(_transactionPath);
            return;
        }

        throw new InvalidDataException("Unknown Forge recovery transaction operation.");
    }

    public async Task<PatchState> ApplyCoordinatedAsync(
        PatchEngine engine,
        TargetAnalysis analysis,
        IReadOnlyCollection<string> patchIds,
        CancellationToken cancellationToken = default)
    {
        var requiresForge = RequiresExtendedStats(patchIds);
        if (!requiresForge)
        {
            await ValidateVanillaForgeWithoutManagedStateAsync(analysis.Path, cancellationToken);
            return await engine.ApplyAsync(analysis, patchIds, cancellationToken);
        }

        var prepared = await PrepareApplyAsync(analysis.Path, cancellationToken);
        PatchState? exeState = null;
        try
        {
            exeState = await engine.ApplyAsync(analysis, patchIds, cancellationToken);
            await UpdateTransactionPhaseAsync("ExeCommitted", cancellationToken);
            await CommitPreparedApplyAsync(prepared, cancellationToken);
            return exeState;
        }
        catch (Exception originalError)
        {
            try
            {
                await RollBackApplyAsync(prepared, engine, analysis.Path, cancellationToken);
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    "MAYHEM installation failed and automatic rollback also failed. Recovery state was kept for the next installer run.",
                    originalError,
                    rollbackError);
            }

            throw;
        }
    }

    public async Task RestoreCoordinatedAsync(
        PatchEngine engine,
        string exePath,
        CancellationToken cancellationToken = default)
    {
        var fullExePath = Path.GetFullPath(exePath);
        await RecoverInterruptedAsync(fullExePath, engine, cancellationToken);

        var forgePath = ResolveForgePath(fullExePath);
        var forgeState = LoadState();
        var engineState = LoadEngineState();
        var matchingEngineState = engineState is not null && PathsEqual(engineState.GameExePath, fullExePath)
            ? engineState
            : null;

        if (forgeState is null)
        {
            var identity = await GetIdentityAsync(forgePath, cancellationToken);
            if (!IsVanilla(identity))
                throw new InvalidDataException("Forge state is missing and DataPC_patch_01.forge is not exact vanilla. Restore aborted fail-closed.");

            // This intentionally supports restoring a MAYHEM 1.0 Level Unlock install,
            // where the EXE state exists but the required Forge layer was never installed.
            if (matchingEngineState is not null)
                await engine.RestoreAsync(fullExePath, cancellationToken);
            return;
        }

        ValidateStateIdentity(forgeState, fullExePath, forgePath);
        await ValidateVanillaBackupAsync(forgeState.BackupPath, cancellationToken);

        var liveIdentity = await GetIdentityAsync(forgePath, cancellationToken);
        if (!IsPatched(liveIdentity) && !IsVanilla(liveIdentity))
            throw new InvalidDataException("Managed Forge does not match either the exact MAYHEM 1.1 or vanilla identity. Restore aborted fail-closed.");

        var stagePath = RestoreStagePath(forgePath);
        EnsureUntrackedTempAbsent(stagePath);
        EnsureFreeSpaceForFile(stagePath, VanillaForgeSize);
        await CopyFileExactAsync(forgeState.BackupPath, stagePath, cancellationToken);
        await RequireIdentityAsync(stagePath, VanillaForgeSize, VanillaForgeSha256, "staged vanilla Forge", cancellationToken);

        var tx = new ForgeLevel255Transaction
        {
            SchemaVersion = TransactionSchemaVersion,
            Operation = "restore",
            Phase = "Prepared",
            GameExePath = fullExePath,
            ForgePath = forgePath,
            BackupPath = forgeState.BackupPath,
            StagePath = stagePath,
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        await SaveTransactionAsync(tx, cancellationToken);

        try
        {
            if (matchingEngineState is not null)
                await engine.RestoreAsync(fullExePath, cancellationToken);
            await UpdateTransactionPhaseAsync("ExeRestored", cancellationToken);

            liveIdentity = await GetIdentityAsync(forgePath, cancellationToken);
            if (IsPatched(liveIdentity))
            {
                File.Replace(stagePath, forgePath, null, ignoreMetadataErrors: true);
            }
            else if (IsVanilla(liveIdentity))
            {
                DeleteExactFileIfExists(stagePath);
            }
            else
            {
                throw new InvalidDataException("Forge changed during restore transaction.");
            }

            await RequireIdentityAsync(forgePath, VanillaForgeSize, VanillaForgeSha256, "restored live Forge", cancellationToken);
            DeleteExactFileIfExists(_statePath);
            DeleteExactFileIfExists(_transactionPath);
        }
        catch
        {
            // Leave transaction + stage in place for deterministic recovery on next run.
            throw;
        }
    }

    public async Task<ForgeVerifyResult> VerifyPairAsync(string exePath, CancellationToken cancellationToken = default)
    {
        var fullExePath = Path.GetFullPath(exePath);
        var forgePath = ResolveForgePath(fullExePath);
        if (!File.Exists(forgePath))
            return new ForgeVerifyResult(false, false, forgePath, null, "Required DataPC_patch_01.forge is missing.", null);

        var engineState = LoadEngineState();
        if (engineState is not null && !PathsEqual(engineState.GameExePath, fullExePath))
            return new ForgeVerifyResult(false, false, forgePath, null, "EXE patch state belongs to another game path.", null);

        var requiresForge = engineState is not null && RequiresExtendedStats(engineState.AppliedPatchIds);
        var forgeState = LoadState();
        var identity = await GetIdentityAsync(forgePath, cancellationToken);

        if (!requiresForge)
        {
            if (forgeState is not null)
                return new ForgeVerifyResult(false, IsPatched(identity), forgePath, identity.Sha256, "Forge managed state exists but the active EXE patch set does not require extended stats.", forgeState.BackupPath);
            if (!IsVanilla(identity))
                return new ForgeVerifyResult(false, IsPatched(identity), forgePath, identity.Sha256, "DataPC_patch_01.forge is not exact vanilla while Level Unlock is not active.", null);
            return new ForgeVerifyResult(true, false, forgePath, identity.Sha256, "Verified exact vanilla Forge.", null);
        }

        if (forgeState is null)
        {
            var legacy = IsVanilla(identity)
                ? "Level Unlock is active in the EXE, but the Forge stat-extension state is missing and the Forge is still vanilla. This is the MAYHEM 1.0 incomplete Level Unlock state; Restore Vanilla before installing 1.1."
                : "Level Unlock is active in the EXE, but the Forge stat-extension state is missing.";
            return new ForgeVerifyResult(false, IsPatched(identity), forgePath, identity.Sha256, legacy, null);
        }

        try
        {
            ValidateStateIdentity(forgeState, fullExePath, forgePath);
            if (!IsPatched(identity))
                return new ForgeVerifyResult(false, false, forgePath, identity.Sha256, "Managed Forge hash does not match the exact MAYHEM 1.1 extended-stat Forge.", forgeState.BackupPath);
            await ValidateVanillaBackupAsync(forgeState.BackupPath, cancellationToken);
            return new ForgeVerifyResult(true, true, forgePath, identity.Sha256, "Verified MAYHEM 1.1 extended-stat Forge and exact vanilla Forge backup.", forgeState.BackupPath);
        }
        catch (Exception ex)
        {
            return new ForgeVerifyResult(false, IsPatched(identity), forgePath, identity.Sha256, ex.Message, forgeState.BackupPath);
        }
    }

    private async Task<PreparedForgeApply> PrepareApplyAsync(string exePath, CancellationToken cancellationToken)
    {
        var fullExePath = Path.GetFullPath(exePath);
        var forgePath = ResolveForgePath(fullExePath);
        if (LoadState() is not null)
            throw new InvalidOperationException("An active MAYHEM Forge state already exists. Restore Vanilla before reinstalling.");
        if (LoadTransaction() is not null)
            throw new InvalidOperationException("A MAYHEM Forge recovery transaction already exists. Analyze first so recovery can complete.");

        await RequireIdentityAsync(forgePath, VanillaForgeSize, VanillaForgeSha256, "source Forge", cancellationToken);
        ValidateEmbeddedDeltaIdentity();

        var backupPath = BackupPath();
        if (File.Exists(backupPath))
        {
            await ValidateVanillaBackupAsync(backupPath, cancellationToken);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            EnsureFreeSpaceForFile(backupPath, VanillaForgeSize);
            await CopyFileExactAsync(forgePath, backupPath, cancellationToken);
            await ValidateVanillaBackupAsync(backupPath, cancellationToken);
        }

        var stagePath = ApplyStagePath(forgePath);
        EnsureUntrackedTempAbsent(stagePath);
        EnsureFreeSpaceForFile(stagePath, PatchedForgeSize);

        try
        {
            await ApplyEmbeddedDeltaAsync(forgePath, stagePath, cancellationToken);
            await RequireIdentityAsync(stagePath, PatchedForgeSize, PatchedForgeSha256, "staged MAYHEM Forge", cancellationToken);

            var tx = new ForgeLevel255Transaction
            {
                SchemaVersion = TransactionSchemaVersion,
                Operation = "apply",
                Phase = "Prepared",
                GameExePath = fullExePath,
                ForgePath = forgePath,
                BackupPath = backupPath,
                StagePath = stagePath,
                StartedAtUtc = DateTimeOffset.UtcNow
            };
            await SaveTransactionAsync(tx, cancellationToken);
            return new PreparedForgeApply(fullExePath, forgePath, backupPath, stagePath);
        }
        catch
        {
            DeleteExactFileIfExists(stagePath);
            throw;
        }
    }

    private async Task CommitPreparedApplyAsync(PreparedForgeApply prepared, CancellationToken cancellationToken)
    {
        await RequireIdentityAsync(prepared.ForgePath, VanillaForgeSize, VanillaForgeSha256, "live pre-commit Forge", cancellationToken);
        await RequireIdentityAsync(prepared.StagePath, PatchedForgeSize, PatchedForgeSha256, "staged MAYHEM Forge", cancellationToken);
        await ValidateVanillaBackupAsync(prepared.BackupPath, cancellationToken);
        await UpdateTransactionPhaseAsync("ForgeCommitStarted", cancellationToken);

        File.Replace(prepared.StagePath, prepared.ForgePath, null, ignoreMetadataErrors: true);
        await RequireIdentityAsync(prepared.ForgePath, PatchedForgeSize, PatchedForgeSha256, "live MAYHEM Forge", cancellationToken);

        var state = new ForgeLevel255State
        {
            SchemaVersion = StateSchemaVersion,
            GameExePath = prepared.GameExePath,
            ForgePath = prepared.ForgePath,
            BackupPath = prepared.BackupPath,
            OriginalSha256 = VanillaForgeSha256,
            PatchedSha256 = PatchedForgeSha256,
            AppliedAtUtc = DateTimeOffset.UtcNow
        };
        await SaveStateAsync(state, cancellationToken);
        DeleteExactFileIfExists(_transactionPath);
    }

    private async Task RollBackApplyAsync(
        PreparedForgeApply prepared,
        PatchEngine engine,
        string exePath,
        CancellationToken cancellationToken)
    {
        var identity = await GetIdentityAsync(prepared.ForgePath, cancellationToken);
        if (!IsVanilla(identity))
        {
            await ValidateVanillaBackupAsync(prepared.BackupPath, cancellationToken);
            await RestoreForgeFromBackupAsync(prepared.ForgePath, prepared.BackupPath, cancellationToken);
        }

        var engineState = LoadEngineState();
        if (engineState is not null && PathsEqual(engineState.GameExePath, Path.GetFullPath(exePath)))
            await engine.RestoreAsync(exePath, cancellationToken);

        DeleteExactFileIfExists(prepared.StagePath);
        DeleteExactFileIfExists(_statePath);
        DeleteExactFileIfExists(_transactionPath);
    }

    private async Task ValidateVanillaForgeWithoutManagedStateAsync(string exePath, CancellationToken cancellationToken)
    {
        if (LoadState() is not null || LoadTransaction() is not null)
            throw new InvalidOperationException("A MAYHEM Forge state exists. Restore Vanilla before installing a Level Unlock Off configuration.");
        var forgePath = ResolveForgePath(Path.GetFullPath(exePath));
        await RequireIdentityAsync(forgePath, VanillaForgeSize, VanillaForgeSha256, "vanilla Forge for Level Unlock Off", cancellationToken);
    }

    private async Task RestoreForgeFromBackupAsync(string forgePath, string backupPath, CancellationToken cancellationToken)
    {
        await ValidateVanillaBackupAsync(backupPath, cancellationToken);
        var stagePath = RestoreStagePath(forgePath);
        EnsureUntrackedTempAbsent(stagePath);
        EnsureFreeSpaceForFile(stagePath, VanillaForgeSize);
        try
        {
            await CopyFileExactAsync(backupPath, stagePath, cancellationToken);
            await RequireIdentityAsync(stagePath, VanillaForgeSize, VanillaForgeSha256, "staged vanilla Forge", cancellationToken);
            File.Replace(stagePath, forgePath, null, ignoreMetadataErrors: true);
            await RequireIdentityAsync(forgePath, VanillaForgeSize, VanillaForgeSha256, "restored Forge", cancellationToken);
        }
        catch
        {
            DeleteExactFileIfExists(stagePath);
            throw;
        }
    }

    private async Task ApplyEmbeddedDeltaAsync(string sourcePath, string outputPath, CancellationToken cancellationToken)
    {
        var compressed = ReadVerifiedDeltaBytes();
        using var compressedStream = new MemoryStream(compressed, writable: false);
        using var patchStream = new BrotliStream(compressedStream, CompressionMode.Decompress, leaveOpen: false);
        using var reader = new BinaryReader(patchStream, Encoding.UTF8, leaveOpen: true);

        var magic = Encoding.ASCII.GetString(reader.ReadBytes(8));
        if (!string.Equals(magic, "MYHFD11\0", StringComparison.Ordinal))
            throw new InvalidDataException("Embedded Forge delta magic mismatch.");
        var version = reader.ReadInt32();
        if (version != 1)
            throw new InvalidDataException("Unsupported embedded Forge delta version.");
        var sourceSize = reader.ReadInt64();
        var targetSize = reader.ReadInt64();
        var sourceSha = Convert.ToHexString(reader.ReadBytes(32));
        var targetSha = Convert.ToHexString(reader.ReadBytes(32));
        var commandCount = reader.ReadInt64();

        if (sourceSize != VanillaForgeSize || targetSize != PatchedForgeSize ||
            !string.Equals(sourceSha, VanillaForgeSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(targetSha, PatchedForgeSha256, StringComparison.OrdinalIgnoreCase) ||
            commandCount <= 0 || commandCount > 10_000_000)
            throw new InvalidDataException("Embedded Forge delta header does not match MAYHEM 1.1 constants.");

        await RequireIdentityAsync(sourcePath, VanillaForgeSize, VanillaForgeSha256, "delta source Forge", cancellationToken);

        await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess))
        await using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[1024 * 1024];
            for (long i = 0; i < commandCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var type = reader.ReadByte();
                switch (type)
                {
                    case 0:
                    {
                        var offset = reader.ReadInt64();
                        var length = reader.ReadInt64();
                        if (offset < 0 || length < 0 || offset > VanillaForgeSize - length)
                            throw new InvalidDataException($"Invalid embedded Forge COPY command {i}.");
                        source.Position = offset;
                        await CopyExactAsync(source, output, length, buffer, cancellationToken);
                        break;
                    }
                    case 1:
                    {
                        var length = reader.ReadInt64();
                        if (length < 0 || length > PatchedForgeSize)
                            throw new InvalidDataException($"Invalid embedded Forge LITERAL command {i}.");
                        await CopyExactAsync(patchStream, output, length, buffer, cancellationToken);
                        break;
                    }
                    default:
                        throw new InvalidDataException($"Unknown embedded Forge command type {type} at {i}.");
                }
            }
            await output.FlushAsync(cancellationToken);
            output.Flush(flushToDisk: true);
        }
    }

    private byte[] ReadVerifiedDeltaBytes()
    {
        using var resource = typeof(ForgeLevel255Manager).Assembly.GetManifestResourceStream(DeltaResourceName)
            ?? throw new InvalidDataException("Embedded MAYHEM 1.1 Forge delta is missing.");
        using var memory = new MemoryStream();
        resource.CopyTo(memory);
        var bytes = memory.ToArray();
        if (bytes.LongLength != EmbeddedDeltaSize)
            throw new InvalidDataException("Embedded Forge delta size mismatch.");
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(sha, EmbeddedDeltaSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Embedded Forge delta SHA-256 mismatch.");
        return bytes;
    }

    private void ValidateEmbeddedDeltaIdentity() => _ = ReadVerifiedDeltaBytes();

    private string ResolveForgePath(string fullExePath) =>
        Path.Combine(Path.GetDirectoryName(fullExePath) ?? throw new InvalidDataException("Game executable has no parent directory."), ForgeFileName);

    private string BackupPath() => Path.Combine(_backupRoot, "ForgeLevels255", VanillaForgeSha256, ForgeFileName);
    private static string ApplyStagePath(string forgePath) => forgePath + ".mayhem.level255.stage.tmp";
    private static string RestoreStagePath(string forgePath) => forgePath + ".mayhem.level255.restore.tmp";

    private async Task ValidateVanillaBackupAsync(string backupPath, CancellationToken cancellationToken) =>
        await RequireIdentityAsync(backupPath, VanillaForgeSize, VanillaForgeSha256, "vanilla Forge backup", cancellationToken);

    private async Task RequireIdentityAsync(string path, long expectedSize, string expectedSha, string label, CancellationToken cancellationToken)
    {
        var identity = await GetIdentityAsync(path, cancellationToken);
        if (identity.Size != expectedSize || !string.Equals(identity.Sha256, expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{label} identity mismatch. Expected {expectedSize} bytes / {expectedSha}, got {identity.Size} bytes / {identity.Sha256 ?? "<missing>"}.");
    }

    private static async Task<FileIdentity> GetIdentityAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return new FileIdentity(-1, null);
        var info = new FileInfo(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return new FileIdentity(info.Length, Convert.ToHexString(hash));
    }

    private static bool IsVanilla(FileIdentity identity) =>
        identity.Size == VanillaForgeSize && string.Equals(identity.Sha256, VanillaForgeSha256, StringComparison.OrdinalIgnoreCase);

    private static bool IsPatched(FileIdentity identity) =>
        identity.Size == PatchedForgeSize && string.Equals(identity.Sha256, PatchedForgeSha256, StringComparison.OrdinalIgnoreCase);

    private static async Task CopyFileExactAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4 * 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, 4 * 1024 * 1024, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        destination.Flush(flushToDisk: true);
    }

    private static async Task CopyExactAsync(Stream source, Stream destination, long length, byte[] buffer, CancellationToken cancellationToken)
    {
        var remaining = length;
        while (remaining > 0)
        {
            var wanted = (int)Math.Min(buffer.Length, remaining);
            var read = await source.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken);
            if (read <= 0)
                throw new EndOfStreamException($"Unexpected EOF with {remaining} bytes remaining.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }
    }

    private void EnsureFreeSpaceForFile(string path, long requiredFileSize)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrWhiteSpace(root))
            return;
        var drive = new DriveInfo(root);
        var required = checked(requiredFileSize + DiskSafetyMargin);
        if (drive.AvailableFreeSpace < required)
            throw new IOException($"Not enough free space on {root}. Need at least {required:N0} free bytes for a verified Forge staging/backup file; available: {drive.AvailableFreeSpace:N0}.");
    }

    private static void EnsureUntrackedTempAbsent(string path)
    {
        if (File.Exists(path))
            throw new IOException("Untracked MAYHEM Forge staging file already exists: " + path);
    }

    private PatchState? LoadEngineState()
    {
        if (!File.Exists(_engineStatePath))
            return null;
        using var stream = File.OpenRead(_engineStatePath);
        return JsonSerializer.Deserialize(stream, AppJsonContext.Default.PatchState)
            ?? throw new InvalidDataException("MAYHEM EXE state is empty or invalid.");
    }

    private ForgeLevel255State? LoadState()
    {
        if (!File.Exists(_statePath))
            return null;
        var json = File.ReadAllText(_statePath);
        var state = JsonSerializer.Deserialize<ForgeLevel255State>(json, _jsonOptions)
            ?? throw new InvalidDataException("MAYHEM Forge state is empty or invalid.");
        if (state.SchemaVersion != StateSchemaVersion)
            throw new InvalidDataException("Unsupported MAYHEM Forge state schema.");
        return state;
    }

    private ForgeLevel255Transaction? LoadTransaction()
    {
        if (!File.Exists(_transactionPath))
            return null;
        var json = File.ReadAllText(_transactionPath);
        var tx = JsonSerializer.Deserialize<ForgeLevel255Transaction>(json, _jsonOptions)
            ?? throw new InvalidDataException("MAYHEM Forge transaction is empty or invalid.");
        if (tx.SchemaVersion != TransactionSchemaVersion)
            throw new InvalidDataException("Unsupported MAYHEM Forge transaction schema.");
        return tx;
    }

    private Task SaveStateAsync(ForgeLevel255State state, CancellationToken cancellationToken) =>
        WriteJsonAtomicAsync(_statePath, state, cancellationToken);

    private Task SaveTransactionAsync(ForgeLevel255Transaction tx, CancellationToken cancellationToken) =>
        WriteJsonAtomicAsync(_transactionPath, tx, cancellationToken);

    private async Task UpdateTransactionPhaseAsync(string phase, CancellationToken cancellationToken)
    {
        var tx = LoadTransaction() ?? throw new InvalidDataException("MAYHEM Forge transaction disappeared during operation.");
        tx.Phase = phase;
        await SaveTransactionAsync(tx, cancellationToken);
    }

    private async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            await File.WriteAllTextAsync(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            DeleteExactFileIfExists(temp);
        }
    }

    private static void ValidateStateIdentity(ForgeLevel255State state, string exePath, string forgePath)
    {
        if (!PathsEqual(state.GameExePath, exePath) || !PathsEqual(state.ForgePath, forgePath))
            throw new InvalidDataException("MAYHEM Forge state belongs to a different game path.");
        if (!string.Equals(state.OriginalSha256, VanillaForgeSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(state.PatchedSha256, PatchedForgeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MAYHEM Forge state contains unexpected file identities.");
    }

    private static void ValidateTransactionIdentity(ForgeLevel255Transaction tx, string exePath)
    {
        if (!PathsEqual(tx.GameExePath, exePath))
            throw new InvalidDataException("MAYHEM Forge transaction belongs to a different executable path.");
    }

    private static ForgeLevel255State CreateStateFromTransaction(ForgeLevel255Transaction tx) => new()
    {
        SchemaVersion = StateSchemaVersion,
        GameExePath = tx.GameExePath,
        ForgePath = tx.ForgePath,
        BackupPath = tx.BackupPath,
        OriginalSha256 = VanillaForgeSha256,
        PatchedSha256 = PatchedForgeSha256,
        AppliedAtUtc = DateTimeOffset.UtcNow
    };

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static void DeleteExactFileIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private sealed record PreparedForgeApply(string GameExePath, string ForgePath, string BackupPath, string StagePath);
    private sealed record FileIdentity(long Size, string? Sha256);
}

internal sealed class ForgeLevel255State
{
    public int SchemaVersion { get; set; }
    public required string GameExePath { get; set; }
    public required string ForgePath { get; set; }
    public required string BackupPath { get; set; }
    public required string OriginalSha256 { get; set; }
    public required string PatchedSha256 { get; set; }
    public DateTimeOffset AppliedAtUtc { get; set; }
}

internal sealed class ForgeLevel255Transaction
{
    public int SchemaVersion { get; set; }
    public required string Operation { get; set; }
    public required string Phase { get; set; }
    public required string GameExePath { get; set; }
    public required string ForgePath { get; set; }
    public required string BackupPath { get; set; }
    public required string StagePath { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
}

internal sealed record ForgeVerifyResult(
    bool IsValid,
    bool IsPatched,
    string ForgePath,
    string? Sha256,
    string Status,
    string? BackupPath);
