using LibreHardwareMonitor.Hardware;
using OptiSensor.Models;
using System.Diagnostics;

namespace OptiSensor.Libre;

internal sealed class LibreSensorReader : IDisposable
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsBatteryEnabled = true,
        IsControllerEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsNetworkEnabled = true,
        IsStorageEnabled = true
    };

    public void Open()
    {
        _computer.Open();
    }

    public LibreSensorSnapshot ReadSnapshot(bool includeAllSensors = false, IReadOnlyCollection<OptiSensorCategory>? includedCategories = null)
    {
        var updateStopwatch = Stopwatch.StartNew();
        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
                subHardware.Update();
        }
        updateStopwatch.Stop();

        HashSet<OptiSensorCategory>? includedCategorySet = null;
        if (includedCategories is not null)
            includedCategorySet = includedCategories.Count == 0 ? [] : includedCategories.ToHashSet();

        var projectionStopwatch = Stopwatch.StartNew();
        var fallbackSensorIdCount = 0;
        var sensors = _computer.Hardware
            .SelectMany(hardware => GetDetectedSensors(hardware, includeAllSensors, includedCategorySet))
            .Select(sensor =>
            {
                if (sensor.SensorId.StartsWith("fallback/", StringComparison.Ordinal))
                    fallbackSensorIdCount++;

                return sensor;
            })
            .Where(sensor => sensor.Value.HasValue)
            .ToArray();
        projectionStopwatch.Stop();

        var duplicateSensorIdCount = sensors
            .GroupBy(sensor => sensor.SensorId, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Count() > 1);

        var metrics = new LibreReadMetrics(
            HardwareCount: _computer.Hardware.Length,
            SensorCount: sensors.Length,
            FallbackSensorIdCount: fallbackSensorIdCount,
            DuplicateSensorIdCount: duplicateSensorIdCount,
            UpdateMs: updateStopwatch.ElapsedMilliseconds,
            ProjectionMs: projectionStopwatch.ElapsedMilliseconds);

        return new LibreSensorSnapshot(sensors, metrics);
    }

    public void Dispose()
    {
        _computer.Close();
    }

    private static IEnumerable<DetectedSensorInfo> GetDetectedSensors(IHardware hardware, bool includeAllSensors, HashSet<OptiSensorCategory>? includedCategories)
    {
        foreach (var detectedSensor in GetDetectedSensors(hardware, hardware, includeAllSensors, includedCategories))
            yield return detectedSensor;

        foreach (var subHardware in hardware.SubHardware)
        {
            foreach (var detectedSensor in GetDetectedSensors(hardware, subHardware, includeAllSensors, includedCategories))
                yield return detectedSensor;
        }
    }

    private static IEnumerable<DetectedSensorInfo> GetDetectedSensors(IHardware rootHardware, IHardware sensorHardware, bool includeAllSensors, HashSet<OptiSensorCategory>? includedCategories)
    {
        foreach (var sensor in sensorHardware.Sensors)
        {
            var isSupportedOverlaySensor = SensorClassifier.TryClassify(rootHardware, sensor, out var category, out var unit);
            if (!isSupportedOverlaySensor && !includeAllSensors)
                continue;

            if (!isSupportedOverlaySensor)
                SensorClassifier.DescribeForDisplay(rootHardware, sensor, out category, out unit);

            if (includedCategories is not null && !includedCategories.Contains(category))
                continue;

            yield return new DetectedSensorInfo(
                SensorId: BuildSensorId(sensor),
                HardwareType: rootHardware.HardwareType.ToString(),
                HardwareName: rootHardware.Name,
                SensorType: sensor.SensorType.ToString(),
                SensorName: sensor.Name,
                Category: category,
                Unit: unit,
                Value: sensor.Value);
        }
    }

    private static string BuildSensorId(ISensor sensor)
    {
        var identifier = sensor.Identifier?.ToString();
        if (!string.IsNullOrWhiteSpace(identifier))
            return identifier;

        var hardwareIdentifier = sensor.Hardware?.Identifier?.ToString();
        var sensorName = string.IsNullOrWhiteSpace(sensor.Name) ? "unnamed" : Sanitize(sensor.Name);

        return string.Join(
            "/",
            "fallback",
            string.IsNullOrWhiteSpace(hardwareIdentifier) ? "unknown-hardware" : Sanitize(hardwareIdentifier),
            sensor.SensorType,
            sensorName,
            sensor.Index);
    }

    private static string Sanitize(string value)
    {
        return value.Replace('/', '_').Replace('\\', '_');
    }
}
