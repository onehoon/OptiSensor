using OptiSensor.App;
using OptiSensor.Settings;
using OptiSensor.Tweaks.IntelVrr;

namespace OptiSensor.Tweaks;

/// <summary>
/// Entry point for the Tweaks backend, called from <c>ApplicationHost</c> as an independently
/// tracked fire-and-forget background task. Sensor monitoring/publishing never awaits or depends
/// on this - it is started only after sensor services are already underway, and any failure here
/// is caught and logged, never surfaced as an application-level fault.
/// </summary>
internal static class TweakStartupCoordinator
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15)
    ];

    /// <summary>Runs all registered tweaks once, in the background. Never throws.</summary>
    public static async Task RunAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            await RunIntelVrrRangeFixAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SimpleLog.TryWrite("Tweaks startup canceled.");
        }
        catch (Exception ex)
        {
            SimpleLog.TryWrite($"Tweaks startup failed: {ex.Message}");
            SimpleLog.TryWriteException(ex);
        }
    }

    private static async Task RunIntelVrrRangeFixAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var isEnabled = settings.IntelVrrRangeFixEnabled;

        var tweak = new IntelVrrRangeTweak(
            () => new IntelArcSyncClient(),
            AffectedPanelDetector.EnumeratePanelIdentities);

        // One log per process launch: truncate whatever the previous startup left behind exactly
        // once here, then every attempt below appends to the same session log instead of
        // overwriting it - so a bounded retry sequence keeps the record of earlier attempts.
        IntelVrrRunLogger.StartSession();

        if (!isEnabled)
        {
            // Still run once so the persisted result reflects "Disabled" rather than a stale
            // result from a previous session where it was enabled.
            tweak.Run(false);
            IntelVrrRunLogger.AppendAttempt(1, tweak.LastRunLog);
            return;
        }

        // IGCL can legitimately not be ready yet immediately at startup (driver service still
        // warming up). Bounded, non-blocking retry: initial attempt + a few short backoffs, then
        // give up and report Unavailable - never an infinite watcher.
        IntelVrrRunResult? lastResult = null;
        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lastResult = tweak.Run(isEnabled);
            IntelVrrRunLogger.AppendAttempt(attempt + 1, tweak.LastRunLog);
            if (lastResult.Status != IntelVrrRunStatus.Unavailable)
                return;

            if (attempt < RetryDelays.Length)
                await Task.Delay(RetryDelays[attempt], cancellationToken).ConfigureAwait(false);
        }

        SimpleLog.TryWrite($"Intel VRR Range Fix: {lastResult?.Status} - {lastResult?.Message}");
    }
}
