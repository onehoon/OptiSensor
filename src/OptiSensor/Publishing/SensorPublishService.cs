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
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
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

        sampler.Initialize();
        publisher.Open();

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = ClawTelemetryFormatter.Format(sampler.Sample());

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

            await Task.Delay(Volatile.Read(ref _publishIntervalMs), cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnStatusChanged()
    {
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
