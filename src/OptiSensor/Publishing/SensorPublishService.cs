using OptiSensor.App;
using OptiSensor.Models;

namespace OptiSensor.Publishing;

internal sealed class SensorPublishService : IDisposable
{
    private readonly Func<SensorPublishRunner> _createRunner;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _workerTask;
    private bool _disposed;
    private int _publishIntervalMs = 500;

    public SensorPublishService(Func<SensorPublishRunner> createRunner)
    {
        _createRunner = createRunner;
    }

    public bool IsRunning { get; private set; }
    public string? LastOverlayLine { get; private set; }
    // null means no successful publish snapshot has been observed yet; an empty
    // collection is a real successful snapshot with zero detected sensors.
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
        SimpleLog.TryWrite("Sensor publish service started.");
        OnStatusChanged();

        _workerTask = Task.Run(() => RunLoop(_cancellationTokenSource.Token));
    }

    /// <summary>
    /// Applies a new publish interval to the currently running (or not-yet-started)
    /// worker without restarting it. Takes effect on the next publish iteration.
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
        SimpleLog.TryWrite("Sensor publish service stopped.");
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
                using var runner = _createRunner();
                runner.Open();
                await runner.RunLoopAsync(() => Volatile.Read(ref _publishIntervalMs), OnPublished, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                LastSensors = null;
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

    private void OnPublished(SensorPublishResult result)
    {
        LastOverlayLine = result.OverlayLine;
        LastSensors = result.Sensors;
        LastDetectedSensorCount = result.DetectedSensorCount;
        EnabledSelectedSensorCount = result.EnabledSelectedSensorCount;
        TotalSelectedSensorCount = result.TotalSelectedSensorCount;
        LastError = null;
        OnStatusChanged();
    }

    private void OnStatusChanged()
    {
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
