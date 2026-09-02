using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ACOdysseyUMM;

internal sealed class PatchEngine
{
    private const int ManifestSchemaVersion = 1;
    private const string ApplyOperation = "apply";
    private const string RestoreOperation = "restore";

    private readonly PatchManifest _manifest;
    private readonly string _statePath;
    private readonly string _transactionPath;
    private readonly string _backupRoot;

    public PatchEngine(PatchManifest manifest, string? dataRoot = null, string? backupRoot = null)
    {
        ValidateManifest(manifest);
        _manifest = manifest;

        dataRoot ??= Path.Combine(AppContext.BaseDirectory, "Data");
        dataRoot = Path.GetFullPath(dataRoot);
        backupRoot = Path.GetFullPath(backupRoot ?? Path.Combine(dataRoot, "Backups"));
        _statePath = Path.Combine(dataRoot, "umm-state.json");
        _transactionPath = Path.Combine(dataRoot, "umm-transaction.json");
        _backupRoot = backupRoot;
    }

    public IReadOnlyList<PatchDefinition> Patches => _manifest.Patches;

    public static PatchManifest LoadManifest(string? path = null)
    {
        Stream stream;
        if (!string.IsNullOrWhiteSpace(path))
        {
            stream = File.OpenRead(path);
        }
        else
        {
            stream = typeof(PatchEngine).Assembly.GetManifestResourceStream("ACOdysseyUMM.Resources.patches.json")
                ?? throw new InvalidDataException("Embedded MAYHEM patch manifest is missing.");
        }

        using (stream)
        {
            var manifest = JsonSerializer.Deserialize(stream, AppJsonContext.Default.PatchManifest)
                ?? throw new InvalidDataException("Embedded MAYHEM patch manifest is empty or invalid.");
            ValidateManifest(manifest);
            return manifest;
        }
    }

    public async Task<TargetAnalysis> AnalyzeAsync(string exePath, CancellationToken cancellationToken = default)
    {
        ValidateExePath(exePath);
        var fullPath = Path.GetFullPath(exePath);
        using var operationLock = AcquireOperationLock(fullPath);
        await RecoverInterruptedTransactionAsync(fullPath, cancellationToken);
        return await AnalyzeCoreAsync(fullPath, cancellationToken);
    }

    public async Task<PatchVerifyResult> VerifyAsync(string exePath, CancellationToken cancellationToken = default)
    {
        ValidateExePath(exePath);
        var fullPath = Path.GetFullPath(exePath);
        using var operationLock = AcquireOperationLock(fullPath);
        await RecoverInterruptedTransactionAsync(fullPath, cancellationToken);

        var sha = await ComputeSha256Async(fullPath, cancellationToken);
        var state = LoadState();
        if (state is null)
        {
            var analysis = await AnalyzeCoreAsync(fullPath, cancellationToken);
            if (analysis.Build is null)
            {
                return new PatchVerifyResult(fullPath, "Unsupported or modified executable.", false, false, null, sha, 0, null);
            }

            var verifiedOperationCount = 0;
            foreach (var patch in _manifest.Patches)
            {
                var target = patch.Targets.SingleOrDefault(t => string.Equals(t.BuildId, analysis.Build.Id, StringComparison.Ordinal));
                if (target is null)
                    continue;

                // Mutually-exclusive implementation variants are allowed to overlap each other in the manifest.
                // Validate each variant independently against the exact vanilla target; selected combinations are
                // still checked together by PreflightAsync/ApplyAsync and overlapping selections remain fail-closed.
                ValidateOperations(target.Operations, analysis.Size);
                await ValidateSignatureBytesAsync(fullPath, target.Operations, patched: false, cancellationToken);
                await ValidateOriginalBytesAsync(fullPath, target.Operations, cancellationToken);
                verifiedOperationCount += target.Operations.Count;
            }

            return new PatchVerifyResult(fullPath, "Verified vanilla executable.", true, false, analysis.Build.Id, sha, verifiedOperationCount, null);
        }

        if (!PathsEqual(state.GameExePath, fullPath))
            return new PatchVerifyResult(fullPath, "Patch state belongs to a different executable path.", false, false, state.BuildId, sha, 0, state.BackupPath);
        if (!string.Equals(sha, state.PatchedSha256, StringComparison.OrdinalIgnoreCase))
            return new PatchVerifyResult(fullPath, "Executable hash does not match the recorded patched state.", false, true, state.BuildId, sha, 0, state.BackupPath);
        if (!File.Exists(state.BackupPath))
            return new PatchVerifyResult(fullPath, "Original backup is missing.", false, true, state.BuildId, sha, 0, state.BackupPath);

        var backupSha = await ComputeSha256Async(state.BackupPath, cancellationToken);
        if (!string.Equals(backupSha, state.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            return new PatchVerifyResult(fullPath, "Original backup hash mismatch.", false, true, state.BuildId, sha, 0, state.BackupPath);

        ValidateBackupMetadata(state.BackupPath, state.GameExePath, state.OriginalSha256, new FileInfo(state.BackupPath).Length);
        var operations = GetOperationsForBuild(state.BuildId, state.AppliedPatchIds);
        ValidateOperations(operations, new FileInfo(fullPath).Length);
        await ValidateSignatureBytesAsync(fullPath, operations, patched: true, cancellationToken);
        await ValidatePatchedBytesAsync(fullPath, operations, cancellationToken);
        return new PatchVerifyResult(fullPath, "Verified AC Odyssey UMM patched executable and original backup.", true, true, state.BuildId, sha, operations.Count, state.BackupPath);
    }

    public IReadOnlyList<PatchDefinition> GetApplicablePatches(TargetAnalysis analysis)
    {
        if (analysis.HasStateConflict)
            return [];

        var buildId = analysis.Build?.Id;
        if (buildId is null && analysis.MatchesSavedPatchedState)
        {
            var state = LoadState();
            if (state is not null && PathsEqual(state.GameExePath, analysis.Path))
                buildId = state.BuildId;
        }

        if (buildId is null)
            return [];

        return _manifest.Patches
            .Where(p => p.Targets.Any(t => string.Equals(t.BuildId, buildId, StringComparison.Ordinal)))
            .ToList();
    }

    public async Task<PatchPreflightResult> PreflightAsync(
        TargetAnalysis analysis,
        IReadOnlyCollection<string> patchIds,
        CancellationToken cancellationToken = default)
    {
        if (analysis.Build is null)
            throw new InvalidOperationException("Target must match a known vanilla build before patch preflight.");
        if (analysis.HasStateConflict)
            throw new InvalidOperationException("An existing patch state conflicts with this target.");
        if (patchIds.Count == 0)
            throw new InvalidOperationException("No patches selected.");

        using var operationLock = AcquireOperationLock(analysis.Path);
        await RecoverInterruptedTransactionAsync(analysis.Path, cancellationToken);

        if (LoadState() is not null)
            throw new InvalidOperationException("An active AC Odyssey UMM patch state already exists. Restore it before preflighting another patch set.");

        var current = await AnalyzeCoreAsync(analysis.Path, cancellationToken);
        if (current.Build is null || current.HasStateConflict ||
            !string.Equals(current.Build.Id, analysis.Build.Id, StringComparison.Ordinal) ||
            !string.Equals(current.Sha256, analysis.Sha256, StringComparison.OrdinalIgnoreCase) ||
            current.Size != analysis.Size ||
            current.PeTimestamp != analysis.PeTimestamp)
        {
            throw new InvalidOperationException("Target executable changed after analysis. Re-analyze before patch preflight.");
        }

        var requestedIds = patchIds.ToHashSet(StringComparer.Ordinal);
        if (requestedIds.Count != patchIds.Count)
            throw new InvalidOperationException("Duplicate patch IDs were selected.");

        var selected = _manifest.Patches.Where(p => requestedIds.Contains(p.Id)).ToList();
        if (selected.Count != requestedIds.Count)
            throw new InvalidOperationException("One or more selected patch IDs are unknown.");

        var operations = selected.SelectMany(p =>
        {
            var target = p.Targets.SingleOrDefault(t => string.Equals(t.BuildId, current.Build.Id, StringComparison.Ordinal));
            if (target is null)
                throw new InvalidOperationException($"Patch '{p.Id}' has no target for build '{current.Build.Id}'.");
            return target.Operations;
        }).OrderBy(x => x.FileOffset).ToList();

        if (operations.Count == 0)
            throw new InvalidOperationException("Selected patch set contains no operations.");

        ValidateOperations(operations, current.Size);
        await ValidateSignatureBytesAsync(current.Path, operations, patched: false, cancellationToken);
        await ValidateOriginalBytesAsync(current.Path, operations, cancellationToken);

        return new PatchPreflightResult(
            current.Path,
            current.Build.Id,
            requestedIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            operations.Count,
            current.Sha256);
    }

    public async Task<PatchState> ApplyAsync(
        TargetAnalysis analysis,
        IReadOnlyCollection<string> patchIds,
        CancellationToken cancellationToken = default)
    {
        if (analysis.Build is null)
            throw new InvalidOperationException("Target must match a known vanilla build before applying patches.");
        if (analysis.HasStateConflict)
            throw new InvalidOperationException("An existing patch state conflicts with this target. Resolve or restore it before applying patches.");
        if (patchIds.Count == 0)
            throw new InvalidOperationException("No patches selected.");

        using var operationLock = AcquireOperationLock(analysis.Path);
        EnsureGameNotRunning();
        await RecoverInterruptedTransactionAsync(analysis.Path, cancellationToken);

        if (LoadState() is not null)
            throw new InvalidOperationException("An active AC Odyssey UMM patch state already exists. Restore it before applying another patch set.");

        var current = await AnalyzeCoreAsync(analysis.Path, cancellationToken);
        if (current.Build is null || current.HasStateConflict ||
            !string.Equals(current.Build.Id, analysis.Build.Id, StringComparison.Ordinal) ||
            !string.Equals(current.Sha256, analysis.Sha256, StringComparison.OrdinalIgnoreCase) ||
            current.Size != analysis.Size ||
            current.PeTimestamp != analysis.PeTimestamp)
        {
            throw new InvalidOperationException("Target executable changed after analysis. Re-analyze before applying patches.");
        }

        var requestedIds = patchIds.ToHashSet(StringComparer.Ordinal);
        if (requestedIds.Count != patchIds.Count)
            throw new InvalidOperationException("Duplicate patch IDs were selected.");

        var selected = _manifest.Patches.Where(p => requestedIds.Contains(p.Id)).ToList();
        if (selected.Count != requestedIds.Count)
            throw new InvalidOperationException("One or more selected patch IDs are unknown.");

        var operations = selected.SelectMany(p =>
        {
            var target = p.Targets.SingleOrDefault(t => string.Equals(t.BuildId, current.Build.Id, StringComparison.Ordinal));
            if (target is null)
                throw new InvalidOperationException($"Patch '{p.Id}' has no target for build '{current.Build.Id}'.");
            return target.Operations;
        }).OrderBy(x => x.FileOffset).ToList();

        if (operations.Count == 0)
            throw new InvalidOperationException("Selected patch set contains no operations.");

        ValidateOperations(operations, current.Size);
        await ValidateSignatureBytesAsync(current.Path, operations, patched: false, cancellationToken);
        await ValidateOriginalBytesAsync(current.Path, operations, cancellationToken);

        var backupPath = await EnsureBackupAsync(current, cancellationToken);
        var tempPath = current.Path + ".umm.tmp";
        var rollbackPath = current.Path + ".umm.rollback.tmp";
        DeleteFileIfExists(tempPath);
        DeleteFileIfExists(rollbackPath);

        var replacementCommitted = false;
        var journalWritten = false;
        try
        {
            await CopyFileDurablyAsync(current.Path, tempPath, cancellationToken);
            var stagingOriginalSha = await ComputeSha256Async(tempPath, cancellationToken);
            if (!string.Equals(stagingOriginalSha, current.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Staging copy does not match the analyzed executable.");

            await WriteOperationsAsync(tempPath, operations, cancellationToken);
            await ValidatePatchedBytesAsync(tempPath, operations, cancellationToken);
            var patchedSha = await ComputeSha256Async(tempPath, cancellationToken);

            EnsureGameNotRunning();
            var preCommitSha = await ComputeSha256Async(current.Path, cancellationToken);
            if (!string.Equals(preCommitSha, current.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Target executable changed during patch preparation. Commit aborted.");

            var transaction = new PatchTransaction
            {
                Operation = ApplyOperation,
                BuildId = current.Build.Id,
                GameExePath = current.Path,
                OriginalSha256 = current.Sha256,
                PatchedSha256 = patchedSha,
                BackupPath = backupPath,
                RollbackPath = rollbackPath,
                AppliedPatchIds = requestedIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                StartedAtUtc = DateTimeOffset.UtcNow
            };
            SaveTransaction(transaction);
            journalWritten = true;

            ReplaceFilePreservingReadOnly(tempPath, current.Path, rollbackPath);
            replacementCommitted = true;

            try
            {
                var postWriteSha = await ComputeSha256Async(current.Path, CancellationToken.None);
                if (!string.Equals(postWriteSha, patchedSha, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Post-write SHA-256 verification failed.");

                var state = CreateStateFromTransaction(transaction);
                SaveState(state);
                DeleteTransaction();
                journalWritten = false;
                TryDeleteFile(rollbackPath);
                return state;
            }
            catch (Exception commitError)
            {
                try
                {
                    await RollBackReplacementAsync(rollbackPath, current.Path, current.Sha256, CancellationToken.None);
                    TryDeleteFile(_statePath);
                    DeleteTransaction();
                    journalWritten = false;
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("Patch commit failed and automatic rollback also failed. Recovery journal was preserved.", commitError, rollbackError);
                }

                throw;
            }
        }
        finally
        {
            TryDeleteFile(tempPath);
            if (!replacementCommitted)
                TryDeleteFile(rollbackPath);
            if (journalWritten && !replacementCommitted)
            {
                DeleteTransaction();
                journalWritten = false;
            }
        }
    }

    public async Task RestoreAsync(string exePath, CancellationToken cancellationToken = default)
    {
        ValidateExePath(exePath);
        var fullPath = Path.GetFullPath(exePath);
        using var operationLock = AcquireOperationLock(fullPath);
        EnsureGameNotRunning();
        await RecoverInterruptedTransactionAsync(fullPath, cancellationToken);

        var state = LoadState() ?? throw new InvalidOperationException("No AC Odyssey UMM patch state exists.");
        if (!PathsEqual(state.GameExePath, fullPath))
            throw new InvalidOperationException("Saved patch state belongs to a different executable path.");
        if (!File.Exists(state.BackupPath))
            throw new FileNotFoundException("Original backup is missing.", state.BackupPath);

        var backupSha = await ComputeSha256Async(state.BackupPath, cancellationToken);
        if (!string.Equals(backupSha, state.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Backup SHA-256 does not match the recorded original hash.");
        ValidateBackupMetadata(state.BackupPath, state.GameExePath, state.OriginalSha256, new FileInfo(state.BackupPath).Length);

        var currentSha = await ComputeSha256Async(fullPath, cancellationToken);
        if (!string.Equals(currentSha, state.PatchedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Current executable is not the exact state produced by this patcher. Restore aborted.");

        var tempPath = fullPath + ".umm.restore.tmp";
        var rollbackPath = fullPath + ".umm.restore.rollback.tmp";
        DeleteFileIfExists(tempPath);
        DeleteFileIfExists(rollbackPath);

        var replacementCommitted = false;
        var journalWritten = false;
        try
        {
            await CopyFileDurablyAsync(state.BackupPath, tempPath, cancellationToken);
            var tempSha = await ComputeSha256Async(tempPath, cancellationToken);
            if (!string.Equals(tempSha, state.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Restore staging verification failed.");

            EnsureGameNotRunning();
            var preCommitSha = await ComputeSha256Async(fullPath, cancellationToken);
            if (!string.Equals(preCommitSha, state.PatchedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Patched executable changed during restore preparation. Commit aborted.");

            var transaction = new PatchTransaction
            {
                Operation = RestoreOperation,
                BuildId = state.BuildId,
                GameExePath = fullPath,
                OriginalSha256 = state.OriginalSha256,
                PatchedSha256 = state.PatchedSha256,
                BackupPath = state.BackupPath,
                RollbackPath = rollbackPath,
                AppliedPatchIds = state.AppliedPatchIds.ToList(),
                StartedAtUtc = DateTimeOffset.UtcNow
            };
            SaveTransaction(transaction);
            journalWritten = true;

            ReplaceFilePreservingReadOnly(tempPath, fullPath, rollbackPath);
            replacementCommitted = true;

            try
            {
                var restoredSha = await ComputeSha256Async(fullPath, CancellationToken.None);
                if (!string.Equals(restoredSha, state.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Post-restore SHA-256 verification failed.");

                DeleteState();
                DeleteTransaction();
                journalWritten = false;
                TryDeleteFile(rollbackPath);
            }
            catch (Exception restoreError)
            {
                try
                {
                    await RollBackReplacementAsync(rollbackPath, fullPath, state.PatchedSha256, CancellationToken.None);
                    DeleteTransaction();
                    journalWritten = false;
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("Restore commit failed and automatic rollback also failed. Recovery journal was preserved.", restoreError, rollbackError);
                }

                throw;
            }
        }
        finally
        {
            TryDeleteFile(tempPath);
            if (!replacementCommitted)
                TryDeleteFile(rollbackPath);
            if (journalWritten && !replacementCommitted)
            {
                DeleteTransaction();
                journalWritten = false;
            }
        }
    }

    private async Task<TargetAnalysis> AnalyzeCoreAsync(string fullPath, CancellationToken cancellationToken)
    {
        ValidateExePath(fullPath);

        var info = new FileInfo(fullPath);
        var sha = await ComputeSha256Async(fullPath, cancellationToken);
        var timestamp = ReadPeTimestamp(fullPath);
        var build = _manifest.Builds.FirstOrDefault(x =>
            string.Equals(x.Sha256, sha, StringComparison.OrdinalIgnoreCase) &&
            x.Size == info.Length &&
            x.PeTimestamp == timestamp);

        var state = LoadState();
        var stateTargetsThisExe = state is not null && PathsEqual(state.GameExePath, fullPath);

        // A user or external tool may legitimately restore the exact original vanilla executable
        // without going through this installer's Restore command. If the saved state belongs to this
        // exact path and the current file is the state's exact original SHA on a recognized build,
        // the patch state is stale rather than conflicting. Reconcile it automatically so Analyze
        // returns the real vanilla state instead of permanently locking writes.
        if (stateTargetsThisExe &&
            build is not null &&
            string.Equals(state!.OriginalSha256, sha, StringComparison.OrdinalIgnoreCase))
        {
            DeleteState();
            state = null;
            stateTargetsThisExe = false;
        }

        var matchesPatchedState = stateTargetsThisExe &&
            string.Equals(state!.PatchedSha256, sha, StringComparison.OrdinalIgnoreCase);
        var hasStateConflict = state is not null && !matchesPatchedState;

        var status = matchesPatchedState
            ? "Known AC Odyssey UMM patched state."
            : hasStateConflict
                ? "Existing AC Odyssey UMM patch state conflicts with this executable. Write operations are locked."
                : build is not null
                    ? $"Known vanilla build: {build.DisplayName}"
                    : "Unsupported or modified executable. Write operations are locked.";

        return new TargetAnalysis(fullPath, sha, info.Length, timestamp, build, matchesPatchedState, hasStateConflict, status);
    }

    private async Task RecoverInterruptedTransactionAsync(string selectedExePath, CancellationToken cancellationToken)
    {
        var transaction = LoadTransaction();
        if (transaction is null)
            return;

        if (!PathsEqual(transaction.GameExePath, selectedExePath))
            throw new InvalidDataException("An interrupted AC Odyssey UMM transaction belongs to a different executable path. Recovery is required before other targets can be used.");

        if (!File.Exists(selectedExePath))
            throw new FileNotFoundException("Interrupted transaction target executable is missing.", selectedExePath);

        var currentSha = await ComputeSha256Async(selectedExePath, cancellationToken);
        var state = LoadState();
        var backupSha = File.Exists(transaction.BackupPath)
            ? await ComputeSha256Async(transaction.BackupPath, cancellationToken)
            : null;

        if (!string.Equals(backupSha, transaction.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Interrupted transaction backup is missing or corrupt. Automatic recovery is blocked.");
        ValidateBackupMetadata(transaction.BackupPath, transaction.GameExePath, transaction.OriginalSha256, new FileInfo(transaction.BackupPath).Length);

        if (string.Equals(transaction.Operation, ApplyOperation, StringComparison.Ordinal))
        {
            if (string.Equals(currentSha, transaction.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                if (state is not null)
                {
                    if (!StateMatchesTransaction(state, transaction))
                        throw new InvalidDataException("Interrupted apply journal conflicts with the existing patch state. Automatic recovery is blocked.");
                    DeleteState();
                }

                TryDeleteFile(transaction.RollbackPath);
                DeleteTransaction();
                return;
            }

            if (string.Equals(currentSha, transaction.PatchedSha256, StringComparison.OrdinalIgnoreCase))
            {
                if (state is null)
                {
                    SaveState(CreateStateFromTransaction(transaction));
                }
                else if (!StateMatchesTransaction(state, transaction))
                {
                    throw new InvalidDataException("Interrupted apply journal conflicts with the existing patch state. Automatic recovery is blocked.");
                }

                TryDeleteFile(transaction.RollbackPath);
                DeleteTransaction();
                return;
            }

            throw new InvalidDataException("Interrupted apply transaction found an unknown executable hash. Automatic recovery is blocked.");
        }

        if (string.Equals(transaction.Operation, RestoreOperation, StringComparison.Ordinal))
        {
            if (string.Equals(currentSha, transaction.PatchedSha256, StringComparison.OrdinalIgnoreCase))
            {
                if (state is null)
                {
                    SaveState(CreateStateFromTransaction(transaction));
                }
                else if (!StateMatchesTransaction(state, transaction))
                {
                    throw new InvalidDataException("Interrupted restore journal conflicts with the existing patch state. Automatic recovery is blocked.");
                }

                TryDeleteFile(transaction.RollbackPath);
                DeleteTransaction();
                return;
            }

            if (string.Equals(currentSha, transaction.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                if (state is not null && !StateMatchesTransaction(state, transaction))
                    throw new InvalidDataException("Interrupted restore journal conflicts with the existing patch state. Automatic recovery is blocked.");

                if (state is not null)
                    DeleteState();
                TryDeleteFile(transaction.RollbackPath);
                DeleteTransaction();
                return;
            }

            throw new InvalidDataException("Interrupted restore transaction found an unknown executable hash. Automatic recovery is blocked.");
        }

        throw new InvalidDataException("Interrupted transaction has an unknown operation type.");
    }

    private async Task<string> EnsureBackupAsync(TargetAnalysis analysis, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(_backupRoot, analysis.Build!.Id, analysis.Sha256);
        Directory.CreateDirectory(dir);
        var backupPath = Path.Combine(dir, "ACOdyssey.exe");

        if (File.Exists(backupPath))
        {
            var existingSha = await ComputeSha256Async(backupPath, cancellationToken);
            if (!string.Equals(existingSha, analysis.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Existing backup hash mismatch. Backup store is not trustworthy.");
            EnsureBackupMetadata(analysis, backupPath);
            return backupPath;
        }

        var tempBackupPath = backupPath + ".tmp";
        TryDeleteFile(tempBackupPath);
        try
        {
            await CopyFileDurablyAsync(analysis.Path, tempBackupPath, cancellationToken);
            var stagedBackupSha = await ComputeSha256Async(tempBackupPath, cancellationToken);
            if (!string.Equals(stagedBackupSha, analysis.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Backup staging verification failed.");

            File.Move(tempBackupPath, backupPath, overwrite: false);
            var finalBackupSha = await ComputeSha256Async(backupPath, cancellationToken);
            if (!string.Equals(finalBackupSha, analysis.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Final backup SHA-256 does not match the source executable.");

            EnsureBackupMetadata(analysis, backupPath);
            return backupPath;
        }
        finally
        {
            TryDeleteFile(tempBackupPath);
        }
    }

    private static void EnsureBackupMetadata(TargetAnalysis analysis, string backupPath)
    {
        var metadataPath = Path.Combine(Path.GetDirectoryName(backupPath)!, "backup-metadata.json");
        if (File.Exists(metadataPath))
        {
            ValidateBackupMetadata(backupPath, analysis.Path, analysis.Sha256, analysis.Size);
            return;
        }

        var metadata = new BackupMetadata
        {
            OriginalAbsolutePath = Path.GetFullPath(analysis.Path),
            Size = analysis.Size,
            Sha256 = analysis.Sha256,
            BackupAbsolutePath = Path.GetFullPath(backupPath),
            TimestampUtc = DateTimeOffset.UtcNow,
            OperationId = "exe-original-backup",
            TransactionId = Guid.NewGuid().ToString("N")
        };
        SaveJsonAtomically(metadataPath, stream => JsonSerializer.Serialize(stream, metadata, AppJsonContext.Default.BackupMetadata));
        ValidateBackupMetadata(backupPath, analysis.Path, analysis.Sha256, analysis.Size);
    }

    private static void ValidateBackupMetadata(string backupPath, string originalPath, string originalSha256, long originalSize)
    {
        var metadataPath = Path.Combine(Path.GetDirectoryName(backupPath)!, "backup-metadata.json");
        if (!File.Exists(metadataPath))
            throw new InvalidDataException("Backup metadata is missing.");

        BackupMetadata metadata;
        try
        {
            using var stream = File.OpenRead(metadataPath);
            metadata = JsonSerializer.Deserialize(stream, AppJsonContext.Default.BackupMetadata)
                ?? throw new InvalidDataException("Backup metadata is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Backup metadata is corrupt.", ex);
        }

        if (metadata.SchemaVersion != 1 ||
            metadata.Size != originalSize ||
            !string.Equals(metadata.Sha256, originalSha256, StringComparison.OrdinalIgnoreCase) ||
            !PathsEqual(metadata.OriginalAbsolutePath, originalPath) ||
            !PathsEqual(metadata.BackupAbsolutePath, backupPath) ||
            string.IsNullOrWhiteSpace(metadata.OperationId) ||
            string.IsNullOrWhiteSpace(metadata.TransactionId))
        {
            throw new InvalidDataException("Backup metadata does not match the verified original/backup pair.");
        }
    }

    private static void ValidateManifest(PatchManifest manifest)
    {
        if (manifest.SchemaVersion != ManifestSchemaVersion)
            throw new InvalidDataException($"Unsupported patch manifest schema version: {manifest.SchemaVersion}.");

        var buildIds = new HashSet<string>(StringComparer.Ordinal);
        var buildHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var build in manifest.Builds)
        {
            if (string.IsNullOrWhiteSpace(build.Id) || !buildIds.Add(build.Id))
                throw new InvalidDataException("Build IDs must be non-empty and unique.");
            if (string.IsNullOrWhiteSpace(build.DisplayName))
                throw new InvalidDataException($"Build '{build.Id}' has no display name.");
            if (!IsSha256(build.Sha256) || !buildHashes.Add(build.Sha256))
                throw new InvalidDataException($"Build '{build.Id}' has an invalid or duplicate SHA-256.");
            if (build.Size <= 0)
                throw new InvalidDataException($"Build '{build.Id}' has an invalid file size.");
        }

        var patchIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var patch in manifest.Patches)
        {
            if (string.IsNullOrWhiteSpace(patch.Id) || !patchIds.Add(patch.Id))
                throw new InvalidDataException("Patch IDs must be non-empty and unique.");
            if (string.IsNullOrWhiteSpace(patch.DisplayName))
                throw new InvalidDataException($"Patch '{patch.Id}' has no display name.");
            if (patch.Targets.Count == 0)
                throw new InvalidDataException($"Patch '{patch.Id}' has no build targets.");

            var targetBuildIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in patch.Targets)
            {
                if (!buildIds.Contains(target.BuildId))
                    throw new InvalidDataException($"Patch '{patch.Id}' references unknown build '{target.BuildId}'.");
                if (!targetBuildIds.Add(target.BuildId))
                    throw new InvalidDataException($"Patch '{patch.Id}' contains duplicate target '{target.BuildId}'.");
                if (target.Operations.Count == 0)
                    throw new InvalidDataException($"Patch '{patch.Id}' target '{target.BuildId}' has no operations.");

                var ordered = target.Operations.OrderBy(x => x.FileOffset).ToList();
                long previousEnd = -1;
                foreach (var operation in ordered)
                {
                    var original = ParseHex(operation.OriginalHex);
                    var patched = ParseHex(operation.PatchedHex);
                    if (original.Length == 0 || original.Length != patched.Length)
                        throw new InvalidDataException($"Patch '{patch.Id}' has mismatched or empty byte sequences.");
                    if (operation.FileOffset < 0)
                        throw new InvalidDataException($"Patch '{patch.Id}' contains a negative file offset.");
                    if (operation.SignatureOffset is null || operation.SignatureOffset < 0 || string.IsNullOrWhiteSpace(operation.SignatureHex))
                        throw new InvalidDataException($"Patch '{patch.Id}' operation at 0x{operation.FileOffset:X} has no valid production signature.");

                    var signature = ParseHex(operation.SignatureHex);
                    var relativePatchOffset = operation.FileOffset - operation.SignatureOffset.Value;
                    if (relativePatchOffset < 0 || relativePatchOffset + original.Length > signature.Length)
                        throw new InvalidDataException($"Patch '{patch.Id}' signature does not cover its operation at 0x{operation.FileOffset:X}.");
                    if (!signature.AsSpan((int)relativePatchOffset, original.Length).SequenceEqual(original))
                        throw new InvalidDataException($"Patch '{patch.Id}' signature does not contain the expected original bytes at 0x{operation.FileOffset:X}.");

                    long end;
                    try { end = checked(operation.FileOffset + original.Length); }
                    catch (OverflowException) { throw new InvalidDataException($"Patch '{patch.Id}' contains an overflowing file range."); }

                    if (operation.FileOffset < previousEnd)
                        throw new InvalidDataException($"Patch '{patch.Id}' contains overlapping operations.");
                    previousEnd = end;
                }
            }
        }
    }

    private static void ValidateOperations(IReadOnlyList<PatchOperation> operations, long fileLength)
    {
        long previousEnd = -1;
        foreach (var operation in operations.OrderBy(x => x.FileOffset))
        {
            var original = ParseHex(operation.OriginalHex);
            var patched = ParseHex(operation.PatchedHex);
            if (original.Length == 0 || original.Length != patched.Length)
                throw new InvalidDataException("Patch operation original/patched byte lengths must match and be non-zero.");

            long end;
            try { end = checked(operation.FileOffset + original.Length); }
            catch (OverflowException) { throw new InvalidDataException("Patch operation file range overflowed."); }

            if (operation.FileOffset < 0 || end > fileLength)
                throw new InvalidDataException($"Patch operation at 0x{operation.FileOffset:X} is outside the target file.");
            if (operation.FileOffset < previousEnd)
                throw new InvalidDataException("Selected patch operations overlap.");
            previousEnd = end;
        }
    }

    private List<PatchOperation> GetOperationsForBuild(string buildId, IEnumerable<string> patchIds)
    {
        var requestedIds = patchIds.ToHashSet(StringComparer.Ordinal);
        var selected = _manifest.Patches.Where(p => requestedIds.Contains(p.Id)).ToList();
        if (selected.Count != requestedIds.Count)
            throw new InvalidDataException("Patch state references an unknown patch ID.");

        return selected.SelectMany(p =>
        {
            var target = p.Targets.SingleOrDefault(t => string.Equals(t.BuildId, buildId, StringComparison.Ordinal));
            if (target is null)
                throw new InvalidDataException($"Patch '{p.Id}' has no target for build '{buildId}'.");
            return target.Operations;
        }).OrderBy(x => x.FileOffset).ToList();
    }

    private static async Task ValidateSignatureBytesAsync(
        string path,
        IReadOnlyList<PatchOperation> operations,
        bool patched,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        foreach (var operation in operations)
        {
            if (operation.SignatureOffset is null || string.IsNullOrWhiteSpace(operation.SignatureHex))
                throw new InvalidDataException($"Patch operation at 0x{operation.FileOffset:X} has no production signature.");

            var expected = ParseHex(operation.SignatureHex);
            if (patched)
            {
                var patchBytes = ParseHex(operation.PatchedHex);
                var relativeOffset = checked((int)(operation.FileOffset - operation.SignatureOffset.Value));
                patchBytes.CopyTo(expected.AsSpan(relativeOffset, patchBytes.Length));
            }

            var actual = new byte[expected.Length];
            stream.Position = operation.SignatureOffset.Value;
            await stream.ReadExactlyAsync(actual, cancellationToken);
            if (!actual.AsSpan().SequenceEqual(expected))
                throw new InvalidDataException($"Patch signature verification failed at file offset 0x{operation.SignatureOffset.Value:X}.");
        }
    }

    private static async Task ValidateOriginalBytesAsync(string path, IReadOnlyList<PatchOperation> operations, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        foreach (var operation in operations)
        {
            var expected = ParseHex(operation.OriginalHex);
            var actual = new byte[expected.Length];
            stream.Position = operation.FileOffset;
            await stream.ReadExactlyAsync(actual, cancellationToken);
            if (!actual.AsSpan().SequenceEqual(expected))
                throw new InvalidDataException($"Original byte verification failed at file offset 0x{operation.FileOffset:X}.");
        }
    }

    private static async Task WriteOperationsAsync(string path, IReadOnlyList<PatchOperation> operations, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.WriteThrough);
        foreach (var operation in operations)
        {
            var bytes = ParseHex(operation.PatchedHex);
            stream.Position = operation.FileOffset;
            await stream.WriteAsync(bytes, cancellationToken);
        }
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static async Task ValidatePatchedBytesAsync(string path, IReadOnlyList<PatchOperation> operations, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        foreach (var operation in operations)
        {
            var expected = ParseHex(operation.PatchedHex);
            var actual = new byte[expected.Length];
            stream.Position = operation.FileOffset;
            await stream.ReadExactlyAsync(actual, cancellationToken);
            if (!actual.AsSpan().SequenceEqual(expected))
                throw new IOException($"Patched byte verification failed at file offset 0x{operation.FileOffset:X}.");
        }
    }

    private PatchState? LoadState()
    {
        if (!File.Exists(_statePath))
            return null;

        try
        {
            using var stream = File.OpenRead(_statePath);
            var state = JsonSerializer.Deserialize(stream, AppJsonContext.Default.PatchState)
                ?? throw new InvalidDataException("Patch state file is empty.");
            ValidateState(state);
            return state;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Patch state file is corrupt. Write operations are locked.", ex);
        }
    }

    private void ValidateState(PatchState state)
    {
        var build = _manifest.Builds.FirstOrDefault(x => string.Equals(x.Id, state.BuildId, StringComparison.Ordinal));
        if (build is null || !string.Equals(build.Sha256, state.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Patch state references an unknown or mismatched build.");
        if (!IsSha256(state.OriginalSha256) || !IsSha256(state.PatchedSha256))
            throw new InvalidDataException("Patch state contains an invalid SHA-256 value.");
        if (string.IsNullOrWhiteSpace(state.GameExePath) || !string.Equals(Path.GetFileName(state.GameExePath), "ACOdyssey.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Patch state contains an invalid game executable path.");

        var expectedBackupPath = GetExpectedBackupPath(state.BuildId, state.OriginalSha256);
        if (string.IsNullOrWhiteSpace(state.BackupPath) || !PathsEqual(expectedBackupPath, state.BackupPath))
            throw new InvalidDataException("Patch state backup path is outside the managed backup store.");
        if (state.AppliedPatchIds.Count != state.AppliedPatchIds.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidDataException("Patch state contains duplicate patch IDs.");
    }

    private PatchTransaction? LoadTransaction()
    {
        if (!File.Exists(_transactionPath))
            return null;

        try
        {
            using var stream = File.OpenRead(_transactionPath);
            var transaction = JsonSerializer.Deserialize(stream, AppJsonContext.Default.PatchTransaction)
                ?? throw new InvalidDataException("Transaction journal is empty.");
            ValidateTransaction(transaction);
            return transaction;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Transaction journal is corrupt. Write operations are locked.", ex);
        }
    }

    private void ValidateTransaction(PatchTransaction transaction)
    {
        if (!string.Equals(transaction.Operation, ApplyOperation, StringComparison.Ordinal) &&
            !string.Equals(transaction.Operation, RestoreOperation, StringComparison.Ordinal))
            throw new InvalidDataException("Transaction journal contains an invalid operation type.");

        var build = _manifest.Builds.FirstOrDefault(x => string.Equals(x.Id, transaction.BuildId, StringComparison.Ordinal));
        if (build is null || !string.Equals(build.Sha256, transaction.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Transaction journal references an unknown or mismatched build.");
        if (!IsSha256(transaction.OriginalSha256) || !IsSha256(transaction.PatchedSha256))
            throw new InvalidDataException("Transaction journal contains an invalid SHA-256 value.");
        if (string.IsNullOrWhiteSpace(transaction.GameExePath) || !string.Equals(Path.GetFileName(transaction.GameExePath), "ACOdyssey.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Transaction journal contains an invalid game executable path.");

        var expectedBackupPath = GetExpectedBackupPath(transaction.BuildId, transaction.OriginalSha256);
        if (!PathsEqual(expectedBackupPath, transaction.BackupPath))
            throw new InvalidDataException("Transaction journal backup path is outside the managed backup store.");

        var expectedApplyRollback = transaction.GameExePath + ".umm.rollback.tmp";
        var expectedRestoreRollback = transaction.GameExePath + ".umm.restore.rollback.tmp";
        var expectedRollback = string.Equals(transaction.Operation, ApplyOperation, StringComparison.Ordinal)
            ? expectedApplyRollback
            : expectedRestoreRollback;
        if (!PathsEqual(expectedRollback, transaction.RollbackPath))
            throw new InvalidDataException("Transaction journal rollback path is invalid.");
        if (transaction.AppliedPatchIds.Count != transaction.AppliedPatchIds.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidDataException("Transaction journal contains duplicate patch IDs.");
    }

    private void SaveState(PatchState state)
    {
        ValidateState(state);
        SaveJsonAtomically(_statePath, stream => JsonSerializer.Serialize(stream, state, AppJsonContext.Default.PatchState));
    }

    private void SaveTransaction(PatchTransaction transaction)
    {
        ValidateTransaction(transaction);
        if (File.Exists(_transactionPath))
            throw new InvalidOperationException("Another AC Odyssey UMM transaction journal already exists.");
        SaveJsonAtomically(_transactionPath, stream => JsonSerializer.Serialize(stream, transaction, AppJsonContext.Default.PatchTransaction));
    }

    private static void SaveJsonAtomically(string path, Action<Stream> serialize)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        TryDeleteFile(tempPath);
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                serialize(stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, path, overwrite: false);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private void DeleteState()
    {
        if (File.Exists(_statePath))
            File.Delete(_statePath);
    }

    private void DeleteTransaction()
    {
        if (File.Exists(_transactionPath))
            File.Delete(_transactionPath);
    }

    private static PatchState CreateStateFromTransaction(PatchTransaction transaction) => new()
    {
        BuildId = transaction.BuildId,
        GameExePath = transaction.GameExePath,
        OriginalSha256 = transaction.OriginalSha256,
        BackupPath = transaction.BackupPath,
        PatchedSha256 = transaction.PatchedSha256,
        AppliedPatchIds = transaction.AppliedPatchIds.ToList(),
        AppliedAtUtc = transaction.StartedAtUtc
    };

    private static bool StateMatchesTransaction(PatchState state, PatchTransaction transaction) =>
        string.Equals(state.BuildId, transaction.BuildId, StringComparison.Ordinal) &&
        PathsEqual(state.GameExePath, transaction.GameExePath) &&
        string.Equals(state.OriginalSha256, transaction.OriginalSha256, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(state.PatchedSha256, transaction.PatchedSha256, StringComparison.OrdinalIgnoreCase) &&
        PathsEqual(state.BackupPath, transaction.BackupPath) &&
        state.AppliedPatchIds.OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(transaction.AppliedPatchIds.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);

    private string GetExpectedBackupPath(string buildId, string originalSha256) =>
        Path.Combine(_backupRoot, buildId, originalSha256, "ACOdyssey.exe");

    private static void ReplaceFilePreservingReadOnly(string sourcePath, string destinationPath, string? backupPath)
    {
        var destinationAttributes = File.GetAttributes(destinationPath);
        var wasReadOnly = (destinationAttributes & FileAttributes.ReadOnly) != 0;

        if (wasReadOnly)
            File.SetAttributes(destinationPath, destinationAttributes & ~FileAttributes.ReadOnly);

        try
        {
            File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors: true);
        }
        catch
        {
            if (wasReadOnly && File.Exists(destinationPath))
            {
                try
                {
                    File.SetAttributes(destinationPath, File.GetAttributes(destinationPath) | FileAttributes.ReadOnly);
                }
                catch
                {
                    // Preserve the original replace exception; attribute restoration is best-effort on failure.
                }
            }
            throw;
        }

        if (wasReadOnly && File.Exists(destinationPath))
            File.SetAttributes(destinationPath, File.GetAttributes(destinationPath) | FileAttributes.ReadOnly);
    }

    private static async Task RollBackReplacementAsync(string rollbackPath, string targetPath, string expectedSha, CancellationToken cancellationToken)
    {
        if (!File.Exists(rollbackPath))
            throw new FileNotFoundException("Transactional rollback file is missing.", rollbackPath);

        if (File.Exists(targetPath))
            ReplaceFilePreservingReadOnly(rollbackPath, targetPath, null);
        else
            File.Move(rollbackPath, targetPath, overwrite: false);

        var restoredSha = await ComputeSha256Async(targetPath, cancellationToken);
        if (!string.Equals(restoredSha, expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Automatic rollback SHA-256 verification failed.");
    }

    private static async Task CopyFileDurablyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
        await source.CopyToAsync(destination, 1024 * 1024, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        destination.Flush(flushToDisk: true);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static uint ReadPeTimestamp(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D)
            throw new InvalidDataException("Target is not an MZ executable.");

        stream.Position = 0x3C;
        var peOffset = reader.ReadInt32();
        if (peOffset <= 0 || peOffset > stream.Length - 0x1A)
            throw new InvalidDataException("Invalid PE header offset.");

        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550)
            throw new InvalidDataException("PE signature not found.");

        var machine = reader.ReadUInt16();
        if (machine != 0x8664)
            throw new InvalidDataException("Target is not an x64 PE executable.");

        var sectionCount = reader.ReadUInt16();
        if (sectionCount == 0)
            throw new InvalidDataException("PE executable contains no sections.");

        var timestamp = reader.ReadUInt32();
        reader.ReadUInt32();
        reader.ReadUInt32();
        var optionalHeaderSize = reader.ReadUInt16();
        reader.ReadUInt16();

        if (optionalHeaderSize < 2 || stream.Position + optionalHeaderSize > stream.Length)
            throw new InvalidDataException("Invalid PE optional header size.");

        var optionalMagic = reader.ReadUInt16();
        if (optionalMagic != 0x020B)
            throw new InvalidDataException("Target is not a PE32+ executable.");

        return timestamp;
    }

    private static byte[] ParseHex(string value)
    {
        if (value is null)
            throw new FormatException("Hex byte string is null.");

        var compact = new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (compact.Length == 0 || (compact.Length & 1) != 0 || compact.Any(c => !Uri.IsHexDigit(c)))
            throw new FormatException($"Invalid hex byte string: '{value}'.");
        return Convert.FromHexString(compact);
    }

    private static bool IsSha256(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static void ValidateExePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("ACOdyssey.exe was not found.", path);
        if (!string.Equals(Path.GetFileName(path), "ACOdyssey.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Select ACOdyssey.exe.");
    }

    private static void EnsureGameNotRunning()
    {
        Process[] processes = [];
        try
        {
            processes = Process.GetProcessesByName("ACOdyssey");
            if (processes.Length != 0)
                throw new InvalidOperationException("ACOdyssey.exe is running. Close the game before applying or restoring patches.");
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private static SemaphoreLease AcquireOperationLock(string exePath)
    {
        var normalized = Path.GetFullPath(exePath).ToUpperInvariant();
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        var semaphore = new Semaphore(1, 1, $"Local\\ACOdysseyUMM_{digest}");
        try
        {
            if (!semaphore.WaitOne(0))
                throw new InvalidOperationException("Another AC Odyssey UMM operation is already active for this executable.");
            return new SemaphoreLease(semaphore);
        }
        catch
        {
            semaphore.Dispose();
            throw;
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only. Verification and transaction recovery do not depend on cleanup succeeding.
        }
    }

    private sealed class SemaphoreLease(Semaphore semaphore) : IDisposable
    {
        private Semaphore? _semaphore = semaphore;

        public void Dispose()
        {
            var semaphore = Interlocked.Exchange(ref _semaphore, null);
            if (semaphore is null)
                return;
            semaphore.Release();
            semaphore.Dispose();
        }
    }
}
