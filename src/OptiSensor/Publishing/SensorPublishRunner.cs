using OptiSensor.Libre;
using OptiSensor.Models;
using OptiSensor.Overlay;

namespace OptiSensor.Publishing;

internal sealed class SensorPublishRunner : IDisposable
{
    private readonly LibreSensorReader _sensorReader;
    private readonly OverlayLineBuilder _lineBuilder;
    private readonly ExternalOverlayPublisher _publisher;
    private readonly Func<IReadOnlyCollection<SelectedOverlaySensor>> _selectedSensorsProvider;
    private bool _hadPublishedOverlay;

    public SensorPublishRunner(
        LibreSensorReader sensorReader,
        OverlayLineBuilder lineBuilder,
        ExternalOverlayPublisher publisher,
        Func<IReadOnlyCollection<SelectedOverlaySensor>> selectedSensorsProvider)
    {
        _sensorReader = sensorReader;
        _lineBuilder = lineBuilder;
        _publisher = publisher;
        _selectedSensorsProvider = selectedSensorsProvider;
    }

    public void Open()
    {
        _sensorReader.Open();
        _publisher.Open();
    }

    public SensorPublishResult PublishOnce()
    {
        var snapshot = _sensorReader.ReadSnapshot();
        var selectedSensors = _selectedSensorsProvider();
        var totalSelectedSensorCount = selectedSensors.Count;
        var enabledSelectedSensorCount = selectedSensors.Count(sensor => sensor.Enabled);
        var overlayLine = totalSelectedSensorCount == 0
            ? _lineBuilder.BuildDefaultLine(snapshot)
            : _lineBuilder.BuildLine(snapshot, selectedSensors);

        if (overlayLine is not null)
        {
            _publisher.Publish(overlayLine);
            _hadPublishedOverlay = true;
        }
        else if (_hadPublishedOverlay)
        {
            _publisher.Clear();
            _hadPublishedOverlay = false;
        }

        return new SensorPublishResult(overlayLine, snapshot.Sensors.Count, enabledSelectedSensorCount, totalSelectedSensorCount);
    }

    public async Task RunLoopAsync(int publishIntervalMs, Action<SensorPublishResult> onPublished, CancellationToken cancellationToken)
    {
        var interval = Math.Clamp(publishIntervalMs, 100, 10000);

        while (!cancellationToken.IsCancellationRequested)
        {
            var result = PublishOnce();
            onPublished(result);

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _publisher.Dispose();
        _sensorReader.Dispose();
    }
}
