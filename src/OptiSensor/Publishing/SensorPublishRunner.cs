using OptiSensor.Libre;
using OptiSensor.Models;
using OptiSensor.Overlay;

namespace OptiSensor.Publishing;

internal sealed class SensorPublishRunner : IDisposable
{
    private const int ClearDebounceCycles = 5;
    private const int CachedSensorLifetimeIntervals = 3;
    private readonly ISensorReader _sensorReader;
    private readonly OverlayOutputComposer _outputComposer;
    private readonly ExternalOverlayPublisher _publisher;
    private readonly Func<IReadOnlyCollection<OverlayGroup>> _overlayGroupsProvider;
    private readonly Dictionary<string, CachedSensor> _lastKnownSensorById = new(StringComparer.OrdinalIgnoreCase);
    private bool _hadPublishedOverlay;
    private int _emptyOverlayCycles;

    public SensorPublishRunner(
        ISensorReader sensorReader,
        OverlayOutputComposer outputComposer,
        ExternalOverlayPublisher publisher,
        Func<IReadOnlyCollection<OverlayGroup>> overlayGroupsProvider)
    {
        _sensorReader = sensorReader;
        _outputComposer = outputComposer;
        _publisher = publisher;
        _overlayGroupsProvider = overlayGroupsProvider;
    }

    public void Open()
    {
        _sensorReader.Open();
        _publisher.Open();
    }

    public SensorPublishResult PublishOnce(int publishIntervalMs = 500)
    {
        var snapshot = _sensorReader.ReadSnapshot();
        var overlayGroups = _overlayGroupsProvider();
        var effectiveSnapshot = CreateEffectiveSnapshot(snapshot, overlayGroups, publishIntervalMs);
        var composition = _outputComposer.Compose(effectiveSnapshot, overlayGroups);
        var overlayLine = composition.Line;

        if (overlayLine is not null)
        {
            _publisher.Publish(overlayLine);
            _hadPublishedOverlay = true;
            _emptyOverlayCycles = 0;
        }
        else if (_hadPublishedOverlay)
        {
            _emptyOverlayCycles++;
            if (_emptyOverlayCycles >= ClearDebounceCycles)
            {
                _publisher.Clear();
                _hadPublishedOverlay = false;
                _emptyOverlayCycles = 0;
            }
        }

        return new SensorPublishResult(overlayLine, effectiveSnapshot.Sensors.Count, composition.EnabledSelectedSensorCount, composition.TotalSelectedSensorCount);
    }

    public Task RunLoopAsync(int publishIntervalMs, Action<SensorPublishResult> onPublished, CancellationToken cancellationToken)
    {
        var fixedInterval = Math.Clamp(publishIntervalMs, 100, 2000);
        return RunLoopAsync(() => fixedInterval, onPublished, cancellationToken);
    }

    public async Task RunLoopAsync(Func<int> publishIntervalProvider, Action<SensorPublishResult> onPublished, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var interval = Math.Clamp(publishIntervalProvider(), 100, 2000);
            var result = PublishOnce(interval);
            onPublished(result);

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _publisher.Dispose();
        _sensorReader.Dispose();
    }

    private LibreSensorSnapshot CreateEffectiveSnapshot(
        LibreSensorSnapshot snapshot,
        IReadOnlyCollection<OverlayGroup> overlayGroups,
        int publishIntervalMs)
    {
        var now = Environment.TickCount64;
        var cacheLifetimeMs = Math.Clamp(publishIntervalMs, 100, 2000) * CachedSensorLifetimeIntervals;
        foreach (var sensor in snapshot.Sensors)
            _lastKnownSensorById[sensor.SensorId] = new CachedSensor(sensor, now);

        var expiredSensorIds = _lastKnownSensorById
            .Where(entry => now - entry.Value.LastObservedTick > cacheLifetimeMs)
            .Select(entry => entry.Key)
            .ToArray();
        foreach (var sensorId in expiredSensorIds)
            _lastKnownSensorById.Remove(sensorId);

        var selectedSensorIds = overlayGroups
            .SelectMany(group => group.Sensors)
            .Select(sensor => sensor.SensorId)
            .Where(sensorId => !string.IsNullOrWhiteSpace(sensorId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (selectedSensorIds.Length == 0)
            return snapshot;

        var sensorById = snapshot.Sensors
            .GroupBy(sensor => sensor.SensorId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var sensorId in selectedSensorIds)
        {
            if (sensorById.ContainsKey(sensorId))
                continue;

            if (_lastKnownSensorById.TryGetValue(sensorId, out var cached))
                sensorById[sensorId] = cached.Sensor;
        }

        var mergedSensors = sensorById.Values.ToArray();
        return new LibreSensorSnapshot(mergedSensors, snapshot.Metrics);
    }

    private sealed record CachedSensor(DetectedSensorInfo Sensor, long LastObservedTick);
}
