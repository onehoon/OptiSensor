using OptiSensor.App;

namespace OptiSensor.Publishing;

internal sealed class SensorPublishService : IDisposable
{
    private readonly SensorPublishRunner _runner;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _workerTask;
    private bool _disposed;

    public SensorPublishService(SensorPublishRunner runner)
    {
        _runner = runner;
    }

    public bool IsRunning { get; private set; }
    public string? LastOverlayLine { get; private set; }
    public int LastDetectedSensorCount { get; private set; }
    public int SelectedSensorCount { get; private set; }
    public string? LastError { get; private set; }
    public event EventHandler? StatusChanged;

    public void Start(int publishIntervalMs)
    {
        if (IsRunning)
            return;

        _cancellationTokenSource = new CancellationTokenSource();
        IsRunning = true;
        LastError = null;
        SimpleLog.TryWrite("Sensor publish service started.");
        OnStatusChanged();

        _workerTask = Task.Run(() => RunLoop(publishIntervalMs, _cancellationTokenSource.Token));
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
        _runner.Dispose();
    }

    private async Task RunLoop(int publishIntervalMs, CancellationToken cancellationToken)
    {
        try
        {
            _runner.Open();
            await _runner.RunLoopAsync(publishIntervalMs, OnPublished, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            SimpleLog.TryWriteException(ex);
            OnStatusChanged();
        }
        finally
        {
            IsRunning = false;
            OnStatusChanged();
        }
    }

    private void OnPublished(SensorPublishResult result)
    {
        LastOverlayLine = result.OverlayLine;
        LastDetectedSensorCount = result.DetectedSensorCount;
        SelectedSensorCount = result.SelectedSensorCount;
        LastError = null;
        OnStatusChanged();
    }

    private void OnStatusChanged()
    {
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
