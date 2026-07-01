namespace OptiSensor;

internal sealed class SensorPublishService : IDisposable
{
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _workerTask;

    public bool IsRunning { get; private set; }
    public string? LastOverlayLine { get; private set; }
    public string? LastError { get; private set; }
    public event EventHandler? StatusChanged;

    public void Start(int publishIntervalMs)
    {
        if (IsRunning)
            return;

        var interval = Math.Clamp(publishIntervalMs, 100, 10000);
        _cancellationTokenSource = new CancellationTokenSource();
        IsRunning = true;
        LastError = null;
        SimpleLog.TryWrite("Sensor publish service started.");
        OnStatusChanged();

        _workerTask = Task.Run(() => RunLoop(interval, _cancellationTokenSource.Token));
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
        StopAsync().GetAwaiter().GetResult();
        _cancellationTokenSource?.Dispose();
    }

    private async Task RunLoop(int publishIntervalMs, CancellationToken cancellationToken)
    {
        try
        {
            using var sensorReader = new SensorReader();
            using var publisher = new ExternalOverlayPublisher();

            sensorReader.Open();
            publisher.Open();

            while (!cancellationToken.IsCancellationRequested)
            {
                var overlayLine = sensorReader.ReadOverlayLine();
                if (overlayLine is not null)
                    publisher.Publish(overlayLine);

                LastOverlayLine = overlayLine;
                LastError = null;
                OnStatusChanged();

                await Task.Delay(publishIntervalMs, cancellationToken).ConfigureAwait(false);
            }
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

    private void OnStatusChanged()
    {
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
