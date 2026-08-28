using OptiSensor.App;
using OptiSensor.Claw;
using OptiSensor.Models;
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
    private int _publishIntervalMs = 500;

    // Native read cadence is independent of the shared-memory publish cadence: Core sources
    // every CoreSampleInterval, Battery every fifth Core tick (~5 s).
    private static readonly TimeSpan CoreSampleInterval = TimeSpan.FromSeconds(1);
    private const int BatterySampleEveryNCoreTicks = 5;

    public SensorPublishService()
    {
    }

    public bool IsRunning { get; private set; }
    public string? LastOverlayLine { get; private set; }

    // Legacy HWiNFO sensor-list surface. Kept for UI/compilation compatibility during the native
    // migration; native publishing leaves these neutral. PR #57 removes them with the sensor UI.
    public IReadOnlyList<DetectedSensorInfo>? LastSensors { get; private set; }
    public int LastDetectedSensorCount { get; private set; }
    public int EnabledSelectedSensorCount { get; private set; }
    public int TotalSelectedSensorCount { get; private set; }

    public string? LastError { get; private set; }
    public event EventHandler? StatusChanged;

    public void Start(int publishIntervalMs)
    {
        Volatile.Write(ref _publishIntervalMs, Math.Clamp(publishIntervalMs, 100, 2000));

        if (IsRunning)
            return;

        _cancellationTokenSource = new CancellationTokenSource();
        IsRunning = true;
        LastError = null;
        SimpleLog.TryWrite("Native Claw telemetry publish service started.");
        OnStatusChanged();

        _workerTask = Task.Run(() => RunLoop(_cancellationTokenSource.Token));
    }

    /// <summary>
    /// Applies a new publish interval to the running (or not-yet-started) worker without
    /// restarting it. Takes effect on the next publish iteration.
    /// </summary>
    public void UpdatePublishInterval(int publishIntervalMs)
    {
        var clamped = Math.Clamp(publishIntervalMs, 100, 2000);
        Volatile.Write(ref _publishIntervalMs, clamped);
        SimpleLog.TryWrite($"Publish interval updated to {clamped} ms.");
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
        LastSensors = null;
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
        // not gate this - whatever is present after priming is published.
        sampler.SampleCore();
        sampler.SampleBattery();
        await Task.Delay(CoreSampleInterval, sessionToken).ConfigureAwait(false);
        sampler.SampleCore();

        var samplingTask = RunSamplingLoopAsync(sampler, sessionToken);
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

                await Task.Delay(Volatile.Read(ref _publishIntervalMs), sessionToken).ConfigureAwait(false);
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

    private static async Task RunSamplingLoopAsync(ClawTelemetrySampler sampler, CancellationToken cancellationToken)
    {
        // Runs until the session token is cancelled (task ends Canceled) or a native read throws
        // (task ends Faulted). Either way the publish loop observes completion and ends the
        // session; RunPublishSessionAsync's finally sorts the cancellation case from the fault.
        var coreTicks = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(CoreSampleInterval, cancellationToken).ConfigureAwait(false);
            sampler.SampleCore();

            if (++coreTicks % BatterySampleEveryNCoreTicks == 0)
                sampler.SampleBattery();
        }
    }

    private void OnStatusChanged()
    {
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
