using OptiSensor.OptiScalerUpdate;

namespace OptiSensor.UI;

/// <summary>
/// Pure mapping from the updater core's results to the strings/enabled-state the
/// <see cref="OptiScalerReplaceWindow"/> shows. Kept out of code-behind so the "which discovery
/// status enables Replace" contract is unit-testable without a WPF host.
/// </summary>
internal static class OptiScalerReplacePresentation
{
    /// <summary>Replace is offered only for a confirmed single OptiScaler 0.9 target, and never
    /// while a replacement is already running.</summary>
    public static bool CanReplace(OptiScalerDiscoveryResult? discovery, bool busy) =>
        !busy
        && discovery is { Status: OptiScalerDiscoveryStatus.Found, TargetDllPath: not null and not "" };

    public static string DescribeDiscovery(OptiScalerDiscoveryResult discovery) => discovery.Status switch
    {
        OptiScalerDiscoveryStatus.Found =>
            $"{Path.GetFileName(discovery.TargetDllPath)}\nOptiScaler {discovery.Version}",
        OptiScalerDiscoveryStatus.NotFound =>
            "OptiScaler was not found in the selected folder.",
        OptiScalerDiscoveryStatus.UnsupportedVersion =>
            discovery.Version is null
                ? $"{Path.GetFileName(discovery.TargetDllPath)} is OptiScaler, but its version could not be read. Only OptiScaler 0.9 is supported."
                : $"{Path.GetFileName(discovery.TargetDllPath)} is OptiScaler {discovery.Version}. Only OptiScaler 0.9 is supported.",
        OptiScalerDiscoveryStatus.MultipleFound =>
            "More than one OptiScaler installation was found under the selected folder. "
            + "Select the specific game folder to update:\n"
            + string.Join("\n", discovery.DetectedPaths),
        _ => "Select an existing game folder.",
    };

    public static string DescribeResult(OptiScalerUpdateResult result) => result switch
    {
        { Status: OptiScalerUpdateStatus.Replaced } => "OptiScaler replaced successfully.",
        { Status: OptiScalerUpdateStatus.Skipped } => "OptiScaler is already up to date.",
        { Status: OptiScalerUpdateStatus.Canceled } => "The replacement was canceled.",
        { Reason: OptiScalerUpdateReason.FileInUse } =>
            "OptiScaler is currently in use. Close the game and try again.",
        { Reason: OptiScalerUpdateReason.DownloadFailed or OptiScalerUpdateReason.InvalidArchive or OptiScalerUpdateReason.SourceValidationFailed } =>
            "The latest OptiScaler build could not be downloaded or validated. Your OptiScaler DLL was not changed.",
        { Reason: OptiScalerUpdateReason.TargetMissing or OptiScalerUpdateReason.TargetNotOptiScaler or OptiScalerUpdateReason.UnsupportedTargetVersion } =>
            "The selected OptiScaler DLL changed and is no longer a valid OptiScaler 0.9 target. Select the folder again.",
        _ => result.Message,
    };
}
