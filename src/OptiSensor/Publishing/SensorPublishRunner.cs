using OptiSensor.Libre;
using OptiSensor.Models;
using OptiSensor.Overlay;

namespace OptiSensor.Publishing;

internal sealed class SensorPublishRunner : IDisposable
{
    private const int ClearDebounceCycles = 5;
    private const int CachedSensorLifetimeIntervals = 3;
    private readonly ISensorReader _sensorReader;
    private readonly OverlayLineBuilder _lineBuilder;
    private readonly ExternalOverlayPublisher _publisher;
    private readonly Func<IReadOnlyCollection<OverlayGroup>> _overlayGroupsProvider;
    private readonly Dictionary<string, CachedSensor> _lastKnownSensorById = new(StringComparer.OrdinalIgnoreCase);
    private bool _hadPublishedOverlay;
    private int _emptyOverlayCycles;

    public SensorPublishRunner(
        ISensorReader sensorReader,
        OverlayLineBuilder lineBuilder,
        ExternalOverlayPublisher publisher,
        Func<IReadOnlyCollection<OverlayGroup>> overlayGroupsProvider)
    {
        _sensorReader = sensorReader;
        _lineBuilder = lineBuilder;
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
        var totalSelectedSensorCount = overlayGroups.Sum(group => group.Sensors.Count);
        var enabledSelectedSensorCount = overlayGroups.Where(group => group.Enabled).Sum(group => group.Sensors.Count(sensor => sensor.Enabled));
        var overlayLine = totalSelectedSensorCount == 0
            ? _lineBuilder.BuildDefaultLine(effectiveSnapshot)
            : _lineBuilder.BuildLine(effectiveSnapshot, overlayGroups);

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

        return new SensorPublishResult(overlayLine, effectiveSnapshot.Sensors.Count, enabledSelectedSensorCount, totalSelectedSensorCount);
    }

    public async Task RunLoopAsync(int publishIntervalMs, Action<SensorPublishResult> onPublished, CancellationToken cancellationToken)
    {
        var interval = Math.Clamp(publishIntervalMs, 100, 10000);

        while (!cancellationToken.IsCancellationRequested)
        {
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
        var cacheLifetimeMs = Math.Clamp(publishIntervalMs, 100, 10000) * CachedSensorLifetimeIntervals;
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
