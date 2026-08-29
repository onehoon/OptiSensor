using OptiSensor.App;
using OptiSensor.Claw;
using OptiSensor.Overlay;

namespace OptiSensor.Publishing;

/// <summary>
/// Process-lifetime Claw publishing owner. Each session samples native telemetry
/// (<see cref="ClawTelemetrySampler"/>), formats one plain-text line
/// (<see cref="ClawTelemetryFormatter"/>), and writes it to the OptiScaler external overlay
/// (<see cref="ExternalOverlayPublisher"/>). One sampler instance lives for the whole session so
/// CPU / IGCL sample-to-sample counter state is preserved. On an unexpected fault the session is
/// disposed and recreated after 5 seconds (fresh baselines) - no per-source retry/health layer.
/// </summary>
internal sealed class SensorPublishService : IDisposable
{
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _workerTask;
    private bool _disposed;

    // Fixed 1 s shared-memory heartbeat (the retained snapshot only advances on the ~1 s Core /
    // ~5 s Battery read schedule, so a faster publish would just rewrite the same line). This
    // still leaves ~4 s of margin before OptiScaler's 5 s external-overlay stale timeout.
    private const int PublishIntervalMs = 1000;

    // Native read cadence, scheduled independently of the publish heartbeat: Core and Battery
    // each advance on their own monotonic due-time.
    private const long CoreSampleIntervalMs = 1000;
    private const long BatterySampleIntervalMs = 5000;

    public bool IsRunning { get; private set; }
    public string? LastOverlayLine { get; private set; }
    public string? LastError { get; private set; }
    public event EventHandler? StatusChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRunning)
            return;

        _cancellationTokenSource = new CancellationTokenSource();
        IsRunning = true;
        LastError = null;
        SimpleLog.TryWrite("Native Claw telemetry publish service started.");
        OnStatusChanged();

        _workerTask = Task.Run(() => RunLoop(_cancellationTokenSource.Token));
    }

    public async Task StopAsync()
    {
        if (_cancellationTokenSource is null)
            return;

        _cancellationTokenSource.Cancel();

        if (_workerTask is not null)
        {
            try
            {
                await _workerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        IsRunning = false;
        SimpleLog.TryWrite("Native Claw telemetry publish service stopped.");
        OnStatusChanged();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
        _cancellationTokenSource?.Dispose();
    }

    private async Task RunLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunPublishSessionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Any other exception - including a spurious OperationCanceledException that is
                // not this service stopping - is a session fault: record it and recreate.
                LastError = ex.Message;
                LastOverlayLine = null;
                SimpleLog.TryWriteException(ex);
                OnStatusChanged();

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        IsRunning = false;
        OnStatusChanged();
    }

    private async Task RunPublishSessionAsync(CancellationToken cancellationToken)
    {
        using var sampler = new ClawTelemetrySampler();
        using var publisher = new ExternalOverlayPublisher();

        // The sampling and publish loops are one publishing session: a fault or stop in either
        // must end the other. sessionCts is cancelled when this session ends for any reason.
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sessionToken = sessionCts.Token;

        sampler.Initialize();
        publisher.Open();

        // Immediate startup reads, then a second Core read after one interval so the Windows /
        // IGCL rate counters are primed before normal publishing begins. Unavailable metrics do
        // not gate this - whatever is present after priming is published. Every deadline below is
        // anchored to a timestamp taken *after* a real read, so a slow MSI EC / IGCL warm-up on
        // the Windows-startup path can't make the next read fire immediately.
        sampler.SampleCore();
        sampler.SampleBattery();
        var primedAtMs = Environment.TickCount64;

        await DelayUntilAsync(primedAtMs + CoreSampleIntervalMs, sessionToken).ConfigureAwait(false);
        sampler.SampleCore();
        var warmedAtMs = Environment.TickCount64;

        // Normal Core sampling resumes one full interval after the warm-up read completes; Battery
        // keeps its own schedule from the priming read, skipping any deadline that already elapsed
        // during warm-up (same no-catch-up policy as the loop itself).
        var samplingTask = RunSamplingLoopAsync(
            sampler,
            nextCoreDueMs: warmedAtMs + CoreSampleIntervalMs,
            nextBatteryDueMs: AdvanceDueTime(primedAtMs, BatterySampleIntervalMs, warmedAtMs),
            sessionToken);
        try
        {
            while (!sessionToken.IsCancellationRequested)
            {
                // The sampling loop only ends by session cancellation or a fault. If it has
                // stopped, end this publishing session so RunLoop's 5-second recreate policy
                // runs instead of publishing a frozen snapshot forever.
                if (samplingTask.IsCompleted)
                {
                    await samplingTask.ConfigureAwait(false); // rethrows a fault / cancellation
                    throw new InvalidOperationException("Native telemetry sampling loop stopped unexpectedly.");
                }

                // Publish-only tick: read the retained latest snapshot, never re-sample here.
                var line = ClawTelemetryFormatter.Format(sampler.Latest);

                if (line.Length > 0)
                {
                    publisher.Publish(line);
                    LastOverlayLine = line;
                }
                else
                {
                    // Every native metric unavailable: clear the external line, do not publish an
                    // empty string, and do not keep the previous text as a stale-value cache.
                    publisher.Clear();
                    LastOverlayLine = null;
                }

                LastError = null;
                OnStatusChanged();

                await Task.Delay(PublishIntervalMs, sessionToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // End the sibling loop so a publish-side fault can't leave the sampling loop running.
            sessionCts.Cancel();
            try
            {
                await samplingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (sessionCts.IsCancellationRequested)
            {
            }
            catch when (samplingTask.IsFaulted)
            {
                // A sampling fault already observed by the publish loop is the propagating
                // exception; re-observing it here must not mask the original.
            }
        }
    }

    /// <summary>
    /// One sampling loop, two independent monotonic schedules. Runs until the session token is
    /// cancelled (task ends Canceled) or a native read throws (task ends Faulted); either way the
    /// publish loop observes completion and ends the session, then separates cancellation from
    /// fault during cleanup. The clock is re-read *after* each native sample so a read that
    /// overran one or more intervals skips the missed deadlines instead of firing an immediate
    /// catch-up burst.
    /// </summary>
    private static async Task RunSamplingLoopAsync(
        ClawTelemetrySampler sampler,
        long nextCoreDueMs,
        long nextBatteryDueMs,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (Environment.TickCount64 >= nextCoreDueMs)
            {
                sampler.SampleCore();
                nextCoreDueMs = AdvanceDueTime(nextCoreDueMs, CoreSampleIntervalMs, Environment.TickCount64);
            }

            if (Environment.TickCount64 >= nextBatteryDueMs)
            {
                sampler.SampleBattery();
                nextBatteryDueMs = AdvanceDueTime(nextBatteryDueMs, BatterySampleIntervalMs, Environment.TickCount64);
            }

            await DelayUntilAsync(Math.Min(nextCoreDueMs, nextBatteryDueMs), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Advances a monotonic due-time by whole <paramref name="intervalMs"/> steps until it is past
    /// <paramref name="nowMs"/>. Called with a timestamp taken after the (possibly slow) sample, so
    /// deadlines that already elapsed while sampling are skipped rather than replayed as a burst.
    /// </summary>
    internal static long AdvanceDueTime(long previousDueMs, long intervalMs, long nowMs)
    {
        var next = previousDueMs + intervalMs;
        while (next <= nowMs)
            next += intervalMs;
        return next;
    }

    private static Task DelayUntilAsync(long dueMs, CancellationToken cancellationToken)
    {
        var delayMs = Math.Max(1L, dueMs - Environment.TickCount64);
        return Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
    }

    private void OnStatusChanged()
    {
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
