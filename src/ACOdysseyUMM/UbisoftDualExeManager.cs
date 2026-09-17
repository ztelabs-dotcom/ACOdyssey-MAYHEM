using System.Text.Json;

namespace ACOdysseyUMM;

internal sealed class UbisoftDualExeManager
{
    public const string SteamBuildId = "steam-1.5.6-build-17083392";
    public const string UbisoftPlainBuildId = "ubisoft-connect-1.5.6-acodyssey-3cb92f72";
    public const string UbisoftPlusBuildId = "ubisoft-connect-plus-aco-1.5.6-422439da";

    private const int TransactionSchemaVersion = 1;
    private const string ApplyOperation = "apply";
    private const string RestoreOperation = "restore";
    private const string PlusExeFileName = "ACOdyssey_plus.exe";

    private readonly PatchEngine _plusEngine;
    private readonly string _transactionPath;

    public UbisoftDualExeManager(PatchManifest manifest, string storageRoot, string backupRoot)
    {
        var fullStorageRoot = Path.GetFullPath(storageRoot);
        var fullBackupRoot = Path.GetFullPath(backupRoot);
        _transactionPath = Path.Combine(fullStorageRoot, "mayhem-ubisoft-dual-transaction.json");
        _plusEngine = new PatchEngine(
            manifest,
            fullStorageRoot,
            fullBackupRoot,
            expectedExeFileName: PlusExeFileName,
            stateFileName: "umm-plus-state.json",
            transactionFileName: "umm-plus-transaction.json");
    }

    public static bool IsUbisoftPrimaryBuild(string? buildId) =>
        string.Equals(buildId, UbisoftPlainBuildId, StringComparison.Ordinal);

    public async Task RecoverInterruptedAsync(
        string primaryExePath,
        PatchEngine primaryEngine,
        ForgeLevel255Manager forgeManager,
        CancellationToken cancellationToken = default)
    {
        var tx = LoadTransaction();
        if (tx is null)
            return;

        var fullPrimaryPath = Path.GetFullPath(primaryExePath);
        ValidateTransaction(tx, fullPrimaryPath);

        try
        {
            await RestoreAllUbisoftComponentsToVanillaAsync(
                fullPrimaryPath,
                tx.PlusExePath,
                primaryEngine,
                forgeManager,
                cancellationToken);
            DeleteTransaction();
        }
        catch
        {
            // Keep the installation-level transaction so the next Analyze can retry recovery.
            throw;
        }
    }

    public async Task<UbisoftSecondaryAnalysis> AnalyzeSecondaryAsync(
        PatchEngine primaryEngine,
        TargetAnalysis primaryAnalysis,
        CancellationToken cancellationToken = default)
    {
        var primaryBuildId = primaryEngine.GetEffectiveBuildId(primaryAnalysis);
        if (!IsUbisoftPrimaryBuild(primaryBuildId))
            return new UbisoftSecondaryAnalysis(false, true, false, null, null, "Single-executable target; Ubisoft dual-EXE coordination is not required.");

        var plusPath = ResolvePlusPath(primaryAnalysis.Path);
        if (!File.Exists(plusPath))
            return new UbisoftSecondaryAnalysis(true, false, false, plusPath, null, "Ubisoft Connect build detected, but sibling ACOdyssey_plus.exe is missing. Install is locked fail-closed.");

        TargetAnalysis plusAnalysis;
        try
        {
            plusAnalysis = await _plusEngine.AnalyzeAsync(plusPath, cancellationToken);
        }
        catch (Exception ex)
        {
            return new UbisoftSecondaryAnalysis(true, false, false, plusPath, null, "Ubisoft sibling analysis failed: " + ex.Message);
        }

        var plusBuildId = _plusEngine.GetEffectiveBuildId(plusAnalysis);
        if (!string.Equals(plusBuildId, UbisoftPlusBuildId, StringComparison.Ordinal))
            return new UbisoftSecondaryAnalysis(true, false, plusAnalysis.MatchesSavedPatchedState, plusPath, plusAnalysis, "Sibling ACOdyssey_plus.exe is not the exact supported Ubisoft build. Install is locked fail-closed.");
        if (plusAnalysis.HasStateConflict)
            return new UbisoftSecondaryAnalysis(true, false, plusAnalysis.MatchesSavedPatchedState, plusPath, plusAnalysis, plusAnalysis.Status);

        if (primaryAnalysis.MatchesSavedPatchedState != plusAnalysis.MatchesSavedPatchedState)
            return new UbisoftSecondaryAnalysis(true, false, plusAnalysis.MatchesSavedPatchedState, plusPath, plusAnalysis, "Ubisoft executable pair is only partially patched. Restore/recovery is required before installing.");

        if (primaryAnalysis.MatchesSavedPatchedState)
        {
            var primaryIds = primaryEngine.GetAppliedPatchIdsForPath(primaryAnalysis.Path);
            var plusIds = _plusEngine.GetAppliedPatchIdsForPath(plusPath);
            if (!primaryIds.SequenceEqual(plusIds, StringComparer.Ordinal))
                return new UbisoftSecondaryAnalysis(true, false, true, plusPath, plusAnalysis, "Ubisoft executable pair contains different MAYHEM patch selections. Restore is required.");
        }

        var state = plusAnalysis.MatchesSavedPatchedState ? "patched" : "vanilla";
        return new UbisoftSecondaryAnalysis(true, true, plusAnalysis.MatchesSavedPatchedState, plusPath, plusAnalysis, $"Verified Ubisoft Connect dual-executable {state} state: ACOdyssey.exe + ACOdyssey_plus.exe.");
    }

    public void ValidatePublishedModuleSet(IEnumerable<string> requiredPatchIds)
    {
        var required = requiredPatchIds.Distinct(StringComparer.Ordinal).ToArray();
        var available = _plusEngine.Patches
            .Where(p => p.Targets.Any(t => string.Equals(t.BuildId, UbisoftPlusBuildId, StringComparison.Ordinal)))
            .Select(p => p.Id)
            .ToHashSet(StringComparer.Ordinal);
        var missing = required.Where(x => !available.Contains(x)).ToArray();
        if (missing.Length != 0)
            throw new InvalidDataException("Ubisoft ACOdyssey_plus.exe module manifest is incomplete: " + string.Join(", ", missing));
    }

    public async Task<UbisoftSecondaryVerifyResult> VerifySecondaryAsync(
        PatchEngine primaryEngine,
        string primaryExePath,
        string? primaryBuildId,
        bool primaryIsPatched,
        CancellationToken cancellationToken = default)
    {
        if (!IsUbisoftPrimaryBuild(primaryBuildId))
            return new UbisoftSecondaryVerifyResult(false, true, false, null, null, null, 0, "Ubisoft dual-EXE verification is not required for this target.");

        var plusPath = ResolvePlusPath(primaryExePath);
        if (!File.Exists(plusPath))
            return new UbisoftSecondaryVerifyResult(true, false, false, plusPath, null, null, 0, "Required sibling ACOdyssey_plus.exe is missing.");

        var verify = await _plusEngine.VerifyAsync(plusPath, cancellationToken);
        if (!verify.IsValid || !string.Equals(verify.BuildId, UbisoftPlusBuildId, StringComparison.Ordinal))
            return new UbisoftSecondaryVerifyResult(true, false, verify.IsPatched, plusPath, verify.Sha256, verify.BackupPath, verify.VerifiedOperationCount, "Ubisoft ACOdyssey_plus.exe verification failed: " + verify.Status);
        if (verify.IsPatched != primaryIsPatched)
            return new UbisoftSecondaryVerifyResult(true, false, verify.IsPatched, plusPath, verify.Sha256, verify.BackupPath, verify.VerifiedOperationCount, "Ubisoft executable pair is only partially patched.");

        if (primaryIsPatched)
        {
            var primaryIds = primaryEngine.GetAppliedPatchIdsForPath(primaryExePath);
            var plusIds = _plusEngine.GetAppliedPatchIdsForPath(plusPath);
            if (!primaryIds.SequenceEqual(plusIds, StringComparer.Ordinal))
                return new UbisoftSecondaryVerifyResult(true, false, true, plusPath, verify.Sha256, verify.BackupPath, verify.VerifiedOperationCount, "Ubisoft executable pair contains different MAYHEM patch selections.");
        }

        return new UbisoftSecondaryVerifyResult(true, true, verify.IsPatched, plusPath, verify.Sha256, verify.BackupPath, verify.VerifiedOperationCount, verify.IsPatched
            ? "Verified patched Ubisoft sibling ACOdyssey_plus.exe and original backup."
            : "Verified vanilla Ubisoft sibling ACOdyssey_plus.exe.");
    }

    public async Task<PatchState> ApplyCoordinatedAsync(
        PatchEngine primaryEngine,
        ForgeLevel255Manager forgeManager,
        TargetAnalysis primaryAnalysis,
        IReadOnlyCollection<string> patchIds,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        progress?.Report(0d);
        var primaryBuildId = primaryEngine.GetEffectiveBuildId(primaryAnalysis);
        if (!IsUbisoftPrimaryBuild(primaryBuildId))
            return await forgeManager.ApplyCoordinatedAsync(
                primaryEngine,
                primaryAnalysis,
                patchIds,
                cancellationToken,
                progress);

        if (primaryAnalysis.Build is null || primaryAnalysis.MatchesSavedPatchedState || primaryAnalysis.HasStateConflict)
            throw new InvalidOperationException("Ubisoft primary executable must be an exact vanilla supported build before install.");

        var secondary = await AnalyzeSecondaryAsync(primaryEngine, primaryAnalysis, cancellationToken);
        if (!secondary.Required || !secondary.IsValid || secondary.Analysis?.Build is null || secondary.IsPatched)
            throw new InvalidOperationException(secondary.Status);
        progress?.Report(0.05d);

        await primaryEngine.PreflightAsync(primaryAnalysis, patchIds, cancellationToken);
        await _plusEngine.PreflightAsync(secondary.Analysis, patchIds, cancellationToken);
        progress?.Report(0.10d);

        if (File.Exists(_transactionPath))
            throw new InvalidOperationException("An Ubisoft dual-executable recovery transaction already exists. Analyze first so recovery can complete.");

        var tx = new UbisoftDualTransaction
        {
            SchemaVersion = TransactionSchemaVersion,
            Operation = ApplyOperation,
            Phase = "Prepared",
            PrimaryExePath = Path.GetFullPath(primaryAnalysis.Path),
            PlusExePath = Path.GetFullPath(secondary.Path!),
            AppliedPatchIds = patchIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        SaveTransaction(tx, createNew: true);

        try
        {
            await _plusEngine.ApplyAsync(secondary.Analysis, patchIds, cancellationToken);
            UpdateTransactionPhase("PlusCommitted");
            progress?.Report(0.18d);

            var primaryState = await forgeManager.ApplyCoordinatedAsync(
                primaryEngine,
                primaryAnalysis,
                patchIds,
                cancellationToken,
                ScaledProgress.Slice(progress, 0.18d, 0.88d));
            UpdateTransactionPhase("PrimaryAndForgeCommitted");
            progress?.Report(0.88d);

            // The Forge manager already performed its authoritative post-commit full SHA-256
            // while holding the validated backup immutable during the transaction. Re-hashing
            // the 3.26 GB live Forge + backup again here would add no independent safety proof.
            // The EXEs are small enough that an independent final verify remains cheap.
            var primaryVerify = await primaryEngine.VerifyAsync(primaryAnalysis.Path, cancellationToken);
            progress?.Report(0.94d);
            var plusVerify = await VerifySecondaryAsync(
                primaryEngine,
                primaryAnalysis.Path,
                UbisoftPlainBuildId,
                primaryVerify.IsPatched,
                cancellationToken);
            if (!primaryVerify.IsValid || !primaryVerify.IsPatched || !plusVerify.IsValid || !plusVerify.IsPatched)
                throw new InvalidDataException("Post-install Ubisoft executable verification failed.");

            DeleteTransaction();
            progress?.Report(1d);
            return primaryState;
        }
        catch (Exception originalError)
        {
            try
            {
                await RestoreAllUbisoftComponentsToVanillaAsync(
                    tx.PrimaryExePath,
                    tx.PlusExePath,
                    primaryEngine,
                    forgeManager,
                    CancellationToken.None);
                DeleteTransaction();
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    "Ubisoft dual-executable MAYHEM installation failed and automatic full rollback also failed. Recovery transaction was preserved.",
                    originalError,
                    rollbackError);
            }

            throw;
        }
    }

    public async Task RestoreCoordinatedAsync(
        PatchEngine primaryEngine,
        ForgeLevel255Manager forgeManager,
        string primaryExePath,
        CancellationToken cancellationToken = default)
    {
        var fullPrimaryPath = Path.GetFullPath(primaryExePath);
        var primaryAnalysis = await primaryEngine.AnalyzeAsync(fullPrimaryPath, cancellationToken);
        var primaryBuildId = primaryEngine.GetEffectiveBuildId(primaryAnalysis);
        if (!IsUbisoftPrimaryBuild(primaryBuildId))
        {
            await forgeManager.RestoreCoordinatedAsync(primaryEngine, fullPrimaryPath, cancellationToken);
            return;
        }

        var plusPath = ResolvePlusPath(fullPrimaryPath);
        if (!File.Exists(plusPath))
            throw new FileNotFoundException("Required Ubisoft sibling ACOdyssey_plus.exe is missing. Restore is blocked fail-closed.", plusPath);
        if (File.Exists(_transactionPath))
            throw new InvalidOperationException("An Ubisoft dual-executable recovery transaction already exists. Analyze first so recovery can complete.");

        var tx = new UbisoftDualTransaction
        {
            SchemaVersion = TransactionSchemaVersion,
            Operation = RestoreOperation,
            Phase = "Prepared",
            PrimaryExePath = fullPrimaryPath,
            PlusExePath = plusPath,
            AppliedPatchIds = primaryEngine.GetAppliedPatchIdsForPath(fullPrimaryPath).ToList(),
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        SaveTransaction(tx, createNew: true);

        try
        {
            await RestoreAllUbisoftComponentsToVanillaAsync(fullPrimaryPath, plusPath, primaryEngine, forgeManager, cancellationToken);
            DeleteTransaction();
        }
        catch
        {
            // Preserve transaction for deterministic recovery on the next Analyze.
            throw;
        }
    }

    private async Task RestoreAllUbisoftComponentsToVanillaAsync(
        string primaryExePath,
        string plusExePath,
        PatchEngine primaryEngine,
        ForgeLevel255Manager forgeManager,
        CancellationToken cancellationToken)
    {
        await forgeManager.RecoverInterruptedAsync(primaryExePath, primaryEngine, cancellationToken);

        if (!File.Exists(plusExePath))
            throw new FileNotFoundException("Ubisoft dual-executable recovery cannot continue because ACOdyssey_plus.exe is missing.", plusExePath);

        // Analyze recovers a component-local PatchEngine journal before we decide whether restore is required.
        await _plusEngine.AnalyzeAsync(plusExePath, cancellationToken);
        await primaryEngine.AnalyzeAsync(primaryExePath, cancellationToken);

        var primaryVerify = await primaryEngine.VerifyAsync(primaryExePath, cancellationToken);
        if (!primaryVerify.IsValid)
            throw new InvalidDataException("Ubisoft primary executable is neither a verified vanilla nor managed patched state: " + primaryVerify.Status);

        var plusVerify = await _plusEngine.VerifyAsync(plusExePath, cancellationToken);
        if (!plusVerify.IsValid)
            throw new InvalidDataException("Ubisoft plus executable is neither a verified vanilla nor managed patched state: " + plusVerify.Status);

        // Forge restore also restores the primary EXE when a matching primary PatchEngine state exists.
        await forgeManager.RestoreCoordinatedAsync(primaryEngine, primaryExePath, cancellationToken);

        plusVerify = await _plusEngine.VerifyAsync(plusExePath, cancellationToken);
        if (plusVerify.IsPatched)
            await _plusEngine.RestoreAsync(plusExePath, cancellationToken);

        var primaryAfter = await primaryEngine.AnalyzeAsync(primaryExePath, cancellationToken);
        var plusAfter = await _plusEngine.AnalyzeAsync(plusExePath, cancellationToken);
        var forgeAfter = await forgeManager.VerifyPairAsync(primaryExePath, cancellationToken);
        if (!string.Equals(primaryAfter.Build?.Id, UbisoftPlainBuildId, StringComparison.Ordinal) ||
            primaryAfter.MatchesSavedPatchedState || primaryAfter.HasStateConflict ||
            !string.Equals(plusAfter.Build?.Id, UbisoftPlusBuildId, StringComparison.Ordinal) ||
            plusAfter.MatchesSavedPatchedState || plusAfter.HasStateConflict ||
            !forgeAfter.IsValid || forgeAfter.IsPatched)
        {
            throw new InvalidDataException("Ubisoft full restore did not return both executables and Forge to exact vanilla identities.");
        }
    }

    private string ResolvePlusPath(string primaryExePath)
    {
        var fullPrimary = Path.GetFullPath(primaryExePath);
        if (!string.Equals(Path.GetFileName(fullPrimary), "ACOdyssey.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Ubisoft dual-executable coordination requires ACOdyssey.exe as the selected primary target.");
        var directory = Path.GetDirectoryName(fullPrimary) ?? throw new InvalidDataException("Primary executable has no parent directory.");
        return Path.Combine(directory, PlusExeFileName);
    }

    private UbisoftDualTransaction? LoadTransaction()
    {
        if (!File.Exists(_transactionPath))
            return null;
        try
        {
            using var stream = File.OpenRead(_transactionPath);
            var tx = JsonSerializer.Deserialize(stream, AppJsonContext.Default.UbisoftDualTransaction)
                ?? throw new InvalidDataException("Ubisoft dual-executable transaction is empty.");
            ValidateTransaction(tx, tx.PrimaryExePath);
            return tx;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Ubisoft dual-executable transaction is corrupt. Write operations are locked.", ex);
        }
    }

    private void ValidateTransaction(UbisoftDualTransaction tx, string selectedPrimaryPath)
    {
        if (tx.SchemaVersion != TransactionSchemaVersion)
            throw new InvalidDataException("Unsupported Ubisoft dual-executable transaction schema.");
        if (!string.Equals(tx.Operation, ApplyOperation, StringComparison.Ordinal) &&
            !string.Equals(tx.Operation, RestoreOperation, StringComparison.Ordinal))
            throw new InvalidDataException("Ubisoft dual-executable transaction contains an invalid operation type.");
        if (string.IsNullOrWhiteSpace(tx.Phase))
            throw new InvalidDataException("Ubisoft dual-executable transaction has no phase.");

        var primary = Path.GetFullPath(tx.PrimaryExePath);
        var plus = Path.GetFullPath(tx.PlusExePath);
        var selected = Path.GetFullPath(selectedPrimaryPath);
        if (!PathsEqual(primary, selected) ||
            !string.Equals(Path.GetFileName(primary), "ACOdyssey.exe", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(plus), PlusExeFileName, StringComparison.OrdinalIgnoreCase) ||
            !PathsEqual(Path.GetDirectoryName(primary)!, Path.GetDirectoryName(plus)!) ||
            !PathsEqual(plus, ResolvePlusPath(primary)))
        {
            throw new InvalidDataException("Ubisoft dual-executable transaction paths do not match one exact game directory.");
        }
        if (tx.AppliedPatchIds.Count != tx.AppliedPatchIds.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidDataException("Ubisoft dual-executable transaction contains duplicate patch IDs.");
    }

    private void SaveTransaction(UbisoftDualTransaction tx, bool createNew)
    {
        ValidateTransaction(tx, tx.PrimaryExePath);
        Directory.CreateDirectory(Path.GetDirectoryName(_transactionPath)!);
        if (createNew && File.Exists(_transactionPath))
            throw new InvalidOperationException("Another Ubisoft dual-executable transaction already exists.");

        var tempPath = _transactionPath + ".tmp";
        TryDeleteExactFile(tempPath);
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, tx, AppJsonContext.Default.UbisoftDualTransaction);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, _transactionPath, overwrite: !createNew);
        }
        finally
        {
            TryDeleteExactFile(tempPath);
        }
    }

    private void UpdateTransactionPhase(string phase)
    {
        var tx = LoadTransaction() ?? throw new InvalidDataException("Ubisoft dual-executable transaction disappeared during operation.");
        tx.Phase = phase;
        SaveTransaction(tx, createNew: false);
    }

    private void DeleteTransaction()
    {
        if (File.Exists(_transactionPath))
            File.Delete(_transactionPath);
    }

    private static void TryDeleteExactFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only; the canonical transaction remains the recovery authority.
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}

internal sealed record UbisoftSecondaryAnalysis(
    bool Required,
    bool IsValid,
    bool IsPatched,
    string? Path,
    TargetAnalysis? Analysis,
    string Status);

internal sealed record UbisoftSecondaryVerifyResult(
    bool Required,
    bool IsValid,
    bool IsPatched,
    string? Path,
    string? Sha256,
    string? BackupPath,
    int VerifiedOperationCount,
    string Status);
