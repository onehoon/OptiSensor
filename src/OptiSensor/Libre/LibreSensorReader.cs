using LibreHardwareMonitor.Hardware;
using OptiSensor.App;
using OptiSensor.Models;
using System.Diagnostics;

namespace OptiSensor.Libre;

internal sealed class LibreSensorReader : ISensorReader
{
    private readonly Dictionary<string, DateTimeOffset> _lastHardwareUpdateUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastHardwareErrorLogUtc = new(StringComparer.OrdinalIgnoreCase);
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

    public LibreSensorSnapshot ReadSnapshot(
        bool includeAllSensors = false,
        IReadOnlyCollection<OptiSensorCategory>? includedCategories = null,
        bool fastStart = false)
    {
        var allHardware = _computer.Hardware.ToArray();
        var nowUtc = DateTimeOffset.UtcNow;
        var hardwareToUpdate = allHardware
            .Where(hardware => ShouldUpdateHardware(hardware, nowUtc, fastStart))
            .ToArray();

        var updateStopwatch = Stopwatch.StartNew();
        var updatedHardwareCount = 0;
        foreach (var hardware in hardwareToUpdate)
        {
            try
            {
                hardware.Update();

                foreach (var subHardware in hardware.SubHardware)
                {
                    try
                    {
                        subHardware.Update();
                    }
                    catch (Exception ex)
                    {
                        LogHardwareFailure(subHardware, "update", ex);
                    }
                }

                _lastHardwareUpdateUtc[GetHardwareKey(hardware)] = nowUtc;
                updatedHardwareCount++;
            }
            catch (Exception ex)
            {
                LogHardwareFailure(hardware, "update", ex);
            }
        }
        updateStopwatch.Stop();

        HashSet<OptiSensorCategory>? includedCategorySet = null;
        if (includedCategories is not null)
            includedCategorySet = includedCategories.Count == 0 ? [] : includedCategories.ToHashSet();

        var projectionStopwatch = Stopwatch.StartNew();
        var fallbackSensorIdCount = 0;
        var sensors = new List<DetectedSensorInfo>();
        foreach (var hardware in allHardware)
        {
            try
            {
                foreach (var sensor in GetDetectedSensors(hardware, includeAllSensors, includedCategorySet))
                {
                    if (sensor.SensorId.StartsWith("fallback/", StringComparison.Ordinal))
                        fallbackSensorIdCount++;

                    if (sensor.Value.HasValue)
                        sensors.Add(sensor);
                }
            }
            catch (Exception ex)
            {
                LogHardwareFailure(hardware, "projection", ex);
            }
        }
        projectionStopwatch.Stop();

        var sensorArray = sensors.ToArray();
        var duplicateSensorIdCount = sensorArray
            .GroupBy(sensor => sensor.SensorId, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Count() > 1);

        var metrics = new LibreReadMetrics(
            HardwareCount: allHardware.Length,
            UpdatedHardwareCount: updatedHardwareCount,
            SensorCount: sensorArray.Length,
            FallbackSensorIdCount: fallbackSensorIdCount,
            DuplicateSensorIdCount: duplicateSensorIdCount,
            UpdateMs: updateStopwatch.ElapsedMilliseconds,
            ProjectionMs: projectionStopwatch.ElapsedMilliseconds,
            FastStartApplied: fastStart);

        return new LibreSensorSnapshot(sensorArray, metrics);
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

    private static bool IsFastStartHardware(IHardware hardware)
    {
        // Prioritize hardware that typically returns quickly so first sensor values appear sooner.
        return hardware.HardwareType is HardwareType.Cpu
            or HardwareType.GpuIntel
            or HardwareType.GpuNvidia
            or HardwareType.GpuAmd
            or HardwareType.Memory
            or HardwareType.Battery;
    }

    private bool ShouldUpdateHardware(IHardware hardware, DateTimeOffset nowUtc, bool fastStart)
    {
        if (fastStart && !IsFastStartHardware(hardware))
            return false;

        var key = GetHardwareKey(hardware);
        if (!_lastHardwareUpdateUtc.TryGetValue(key, out var lastUpdatedUtc))
            return true;

        var interval = GetUpdateInterval(hardware.HardwareType);
        return (nowUtc - lastUpdatedUtc) >= interval;
    }

    private static TimeSpan GetUpdateInterval(HardwareType hardwareType)
    {
        return hardwareType switch
        {
            HardwareType.Cpu => TimeSpan.FromSeconds(1),
            HardwareType.GpuIntel => TimeSpan.FromSeconds(1),
            HardwareType.GpuNvidia => TimeSpan.FromSeconds(1),
            HardwareType.GpuAmd => TimeSpan.FromSeconds(1),
            HardwareType.Battery => TimeSpan.FromSeconds(1),
            HardwareType.Memory => TimeSpan.FromSeconds(2),
            HardwareType.Storage => TimeSpan.FromSeconds(5),
            HardwareType.Network => TimeSpan.FromSeconds(5),
            HardwareType.Motherboard => TimeSpan.FromSeconds(5),
            HardwareType.SuperIO => TimeSpan.FromSeconds(5),
            _ => TimeSpan.FromSeconds(3)
        };
    }

    private static string GetHardwareKey(IHardware hardware)
    {
        var identifier = hardware.Identifier?.ToString();
        if (!string.IsNullOrWhiteSpace(identifier))
            return identifier;

        return $"{hardware.HardwareType}/{Sanitize(hardware.Name)}";
    }

    private void LogHardwareFailure(IHardware hardware, string operation, Exception exception)
    {
        var key = $"{operation}:{GetHardwareKey(hardware)}";
        var nowUtc = DateTimeOffset.UtcNow;
        if (_lastHardwareErrorLogUtc.TryGetValue(key, out var lastLoggedUtc) &&
            nowUtc - lastLoggedUtc < TimeSpan.FromMinutes(1))
        {
            return;
        }

        _lastHardwareErrorLogUtc[key] = nowUtc;
        SimpleLog.TryWrite($"Libre sensor {operation} failed for {GetHardwareKey(hardware)}: {exception.Message}");
    }
}
