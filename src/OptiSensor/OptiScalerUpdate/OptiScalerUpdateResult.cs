namespace OptiSensor.OptiScalerUpdate;

internal enum OptiScalerUpdateStatus
{
    /// <summary>The target DLL was replaced with the freshly downloaded patched build.</summary>
    Replaced,

    /// <summary>The target already contained the exact same bytes; nothing was written.</summary>
    Skipped,

    /// <summary>The update did not complete; the existing target is left untouched (or restored).</summary>
    Failed,

    /// <summary>The caller cancelled during the safe pre-replacement work.</summary>
    Canceled,
}

internal enum OptiScalerUpdateReason
{
    None,
    TargetMissing,
    TargetNotOptiScaler,
    UnsupportedTargetVersion,
    TargetChangedDuringUpdate,
    FileInUse,
    DownloadFailed,
    InvalidArchive,
    SourceValidationFailed,
    TemporaryValidationFailed,
    FinalVerificationFailed,
    AccessDenied,
    Canceled,
    UnexpectedFailure,
}

/// <summary>
/// The single result the future UI receives from <see cref="OptiScalerUpdateService.UpdateAsync"/>.
/// <see cref="Message"/> is a short, user-showable sentence; <see cref="Exception"/> is for logging
/// only.
/// </summary>
internal sealed record OptiScalerUpdateResult(
    OptiScalerUpdateStatus Status,
    OptiScalerUpdateReason Reason,
    string Message,
    Exception? Exception = null)
{
    public static OptiScalerUpdateResult Replaced(string version) =>
        new(OptiScalerUpdateStatus.Replaced, OptiScalerUpdateReason.None,
            $"OptiScaler was updated to {version}.");

    public static OptiScalerUpdateResult Skipped() =>
        new(OptiScalerUpdateStatus.Skipped, OptiScalerUpdateReason.None,
            "OptiScaler is already up to date.");

    public static OptiScalerUpdateResult Failed(OptiScalerUpdateReason reason, string message, Exception? exception = null) =>
        new(OptiScalerUpdateStatus.Failed, reason, message, exception);

    public static OptiScalerUpdateResult Canceled() =>
        new(OptiScalerUpdateStatus.Canceled, OptiScalerUpdateReason.Canceled, "The OptiScaler update was canceled.");
}
