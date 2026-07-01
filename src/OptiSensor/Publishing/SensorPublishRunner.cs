using OptiSensor.Libre;
using OptiSensor.Overlay;

namespace OptiSensor.Publishing;

internal sealed class SensorPublishRunner : IDisposable
{
    private readonly LibreSensorReader _sensorReader;
    private readonly OverlayLineBuilder _lineBuilder;
    private readonly ExternalOverlayPublisher _publisher;

    public SensorPublishRunner(
        LibreSensorReader sensorReader,
        OverlayLineBuilder lineBuilder,
        ExternalOverlayPublisher publisher)
    {
        _sensorReader = sensorReader;
        _lineBuilder = lineBuilder;
        _publisher = publisher;
    }

    public void Open()
    {
        _sensorReader.Open();
        _publisher.Open();
    }

    public string? PublishOnce()
    {
        var snapshot = _sensorReader.ReadSnapshot();
        var overlayLine = _lineBuilder.BuildDefaultLine(snapshot);

        if (overlayLine is not null)
            _publisher.Publish(overlayLine);

        return overlayLine;
    }

    public async Task RunLoopAsync(int publishIntervalMs, Action<string?> onPublished, CancellationToken cancellationToken)
    {
        var interval = Math.Clamp(publishIntervalMs, 100, 10000);

        while (!cancellationToken.IsCancellationRequested)
        {
            var overlayLine = PublishOnce();
            onPublished(overlayLine);

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _publisher.Dispose();
        _sensorReader.Dispose();
    }
}
