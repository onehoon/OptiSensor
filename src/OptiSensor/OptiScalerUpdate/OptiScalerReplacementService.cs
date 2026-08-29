using System.Security.Cryptography;

namespace OptiSensor.OptiScalerUpdate;

/// <summary>
/// The safety-critical file swap, adapted from OptiEditor's proven <c>OptiScalerReplacementService</c>
/// and reduced to OptiSensor's product scope: one caller-selected existing OptiScaler 0.9 proxy DLL,
/// replaced in place with a pre-validated source while keeping its current filename. There is no
/// installation discovery, no multi-proxy handling, and no persistent <c>.bak</c> file.
/// </summary>
internal sealed class OptiScalerReplacementService(IFileVersionReader versionReader, OptiScalerBinaryValidator validator)
{
    private const string TempSuffix = ".optisensor.tmp";
    private const string RollbackSuffix = ".optisensor.rollback";

    /// <param name="stagedSourcePath">A staged, already source-validated OptiScaler.dll.</param>
    /// <param name="sourceHash">SHA-256 of <paramref name="stagedSourcePath"/>.</param>
    /// <param name="targetPath">The existing OptiScaler proxy DLL to replace (any proxy filename).</param>
    public async Task<OptiScalerUpdateResult> ReplaceAsync(
        string stagedSourcePath, byte[] sourceHash, string targetPath, CancellationToken cancellationToken)
    {
        string? temporaryPath = null;
        string? rollbackPath = null;
        var replaced = false;
        var keepRollbackFile = false;

        // Once the swap below has happened, every exit path other than a verified success must try
        // to restore the pre-replacement bytes before returning. A restore that itself fails keeps
        // the rollback copy on disk rather than losing it in the finally cleanup.
        OptiScalerUpdateResult Recover(OptiScalerUpdateReason reason, string message, Exception? exception = null)
        {
            if (!replaced || rollbackPath is null)
                return OptiScalerUpdateResult.Failed(reason, message, exception);
            if (TryRestoreRollback(rollbackPath, targetPath))
                return OptiScalerUpdateResult.Failed(reason, message + " The previous OptiScaler.dll was restored.", exception);
            keepRollbackFile = true;
            return OptiScalerUpdateResult.Failed(reason,
                $"{message} The previous OptiScaler.dll could not be restored; a copy was kept at: {rollbackPath}", exception);
        }

        try
        {
            if (!File.Exists(targetPath))
                return OptiScalerUpdateResult.Failed(OptiScalerUpdateReason.TargetMissing,
                    "The selected OptiScaler DLL no longer exists.");

            // Re-validate the target immediately before touching it: identity and version can
            // change externally between the caller picking the file and this call.
            var current = versionReader.Read(targetPath);
            if (!current.LooksLikeOptiScaler)
                return OptiScalerUpdateResult.Failed(OptiScalerUpdateReason.TargetNotOptiScaler,
                    "The selected file is not an OptiScaler binary.");
            if (!current.IsSupportedNineFamily)
                return OptiScalerUpdateResult.Failed(OptiScalerUpdateReason.UnsupportedTargetVersion,
                    $"The selected OptiScaler is version {current.NumericVersion}. Only OptiScaler 0.9 can be updated here.");

            var targetHash = await OptiScalerBinaryValidator.Sha256Async(targetPath, cancellationToken).ConfigureAwait(false);
            if (CryptographicOperations.FixedTimeEquals(sourceHash, targetHash))
                return OptiScalerUpdateResult.Skipped();

            // The target must be exclusively openable - i.e. no game / process has it loaded.
            try
            {
                using (File.Open(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                return OptiScalerUpdateResult.Failed(OptiScalerUpdateReason.FileInUse,
                    "The OptiScaler DLL is in use. Close the game or app using it and try again.", ex);
            }

            temporaryPath = UniquePath(targetPath, TempSuffix);
            await CopyAndFlushAsync(stagedSourcePath, temporaryPath, cancellationToken).ConfigureAwait(false);
            var temporaryHash = await OptiScalerBinaryValidator.Sha256Async(temporaryPath, CancellationToken.None).ConfigureAwait(false);
            if (new FileInfo(temporaryPath).Length != new FileInfo(stagedSourcePath).Length
                || !CryptographicOperations.FixedTimeEquals(sourceHash, temporaryHash)
                || !validator.Validate(temporaryPath).IsValid)
            {
                return OptiScalerUpdateResult.Failed(OptiScalerUpdateReason.TemporaryValidationFailed,
                    "The prepared OptiScaler.dll could not be verified before replacing the target.");
            }

            // Last safe cancellation point. After the swap below, filesystem consistency wins.
            cancellationToken.ThrowIfCancellationRequested();

            rollbackPath = UniquePath(targetPath, RollbackSuffix);
            await CopyAndFlushAsync(targetPath, rollbackPath, cancellationToken).ConfigureAwait(false);

            ReplaceWithoutBackup(temporaryPath, targetPath);
            temporaryPath = null;
            replaced = true;

            // Verify with CancellationToken.None: a cancellation requested in this window must not
            // turn an actually-successful swap into a reported failure.
            var finalHash = File.Exists(targetPath)
                ? await OptiScalerBinaryValidator.Sha256Async(targetPath, CancellationToken.None).ConfigureAwait(false)
                : [];
            if (!File.Exists(targetPath)
                || !CryptographicOperations.FixedTimeEquals(sourceHash, finalHash)
                || !versionReader.Read(targetPath).LooksLikeOptiScaler)
            {
                return Recover(OptiScalerUpdateReason.FinalVerificationFailed,
                    "The replaced OptiScaler.dll could not be verified.");
            }

            return OptiScalerUpdateResult.Replaced(versionReader.Read(targetPath).NumericVersion.ToString());
        }
        catch (OperationCanceledException)
        {
            return replaced
                ? Recover(OptiScalerUpdateReason.Canceled, "The update was canceled.")
                : OptiScalerUpdateResult.Canceled();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Recover(OptiScalerUpdateReason.AccessDenied, "Access to the OptiScaler DLL was denied.", ex);
        }
        catch (IOException ex) when (IsSharingViolation(ex))
        {
            return Recover(OptiScalerUpdateReason.FileInUse,
                "The OptiScaler DLL is in use. Close the game or app using it and try again.", ex);
        }
        catch (Exception ex)
        {
            return Recover(OptiScalerUpdateReason.UnexpectedFailure, "The OptiScaler DLL could not be replaced.", ex);
        }
        finally
        {
            if (temporaryPath is not null)
                try { File.Delete(temporaryPath); } catch (IOException) { }
            if (rollbackPath is not null && !keepRollbackFile)
                try { File.Delete(rollbackPath); } catch (IOException) { }
        }
    }

    private static void ReplaceWithoutBackup(string temporaryPath, string targetPath)
    {
        try { File.Replace(temporaryPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true); }
        catch (PlatformNotSupportedException) { File.Move(temporaryPath, targetPath, overwrite: true); }
    }

    private static bool TryRestoreRollback(string rollbackPath, string targetPath)
    {
        try { File.Copy(rollbackPath, targetPath, overwrite: true); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool IsSharingViolation(IOException exception) =>
        exception.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021);

    private static string UniquePath(string targetPath, string suffix)
    {
        var candidate = targetPath + suffix;
        return File.Exists(candidate) ? $"{candidate}.{Guid.NewGuid():N}" : candidate;
    }

    private static async Task CopyAndFlushAsync(string source, string target, CancellationToken cancellationToken)
    {
        await using var input = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = File.Open(target, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }
}
