using System.Diagnostics;
using Hwinfo.SharedMemory;
using OptiSensor.Libre;
using OptiSensor.Models;

namespace OptiSensor.HWiNFO;

internal sealed class HwInfoSensorReader : ISensorReader
{
    private readonly SharedMemoryReader _reader = new();

    public void Open()
    {
        // SharedMemoryReader manages the mapping lazily and can reopen stale mappings.
    }

    public LibreSensorSnapshot ReadSnapshot(
        bool includeAllSensors = false,
        IReadOnlyCollection<OptiSensorCategory>? includedCategories = null,
        bool fastStart = false)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = _reader.ReadLocal();
        var included = includedCategories is null ? null : includedCategories.ToHashSet();
        var sensors = result.Readings
            .Select(Map)
            .Where(sensor => sensor.Value.HasValue)
            .Where(sensor => included is null || included.Contains(sensor.Category))
            .ToArray();
        stopwatch.Stop();

        var metrics = new LibreReadMetrics(
            HardwareCount: sensors.Select(sensor => sensor.HardwareName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            UpdatedHardwareCount: 0,
            SensorCount: sensors.Length,
            FallbackSensorIdCount: 0,
            DuplicateSensorIdCount: sensors.GroupBy(sensor => sensor.SensorId, StringComparer.OrdinalIgnoreCase).Count(group => group.Count() > 1),
            UpdateMs: 0,
            ProjectionMs: stopwatch.ElapsedMilliseconds,
            FastStartApplied: false);

        return new LibreSensorSnapshot(sensors, metrics);
    }

    public void Dispose() => _reader.Dispose();

    internal static DetectedSensorInfo Map(SensorReading reading)
    {
        var name = string.IsNullOrWhiteSpace(reading.LabelUser) ? reading.LabelOrig : reading.LabelUser;
        var hardware = string.IsNullOrWhiteSpace(reading.Sensor.NameUser) ? reading.Sensor.NameOrig : reading.Sensor.NameUser;
        var type = reading.ReadingType;
        var value = (float)reading.Value;
        var category = Classify(type, name, hardware);
        return new DetectedSensorInfo(
            SensorId: $"hwinfo/{reading.Sensor.Id}/{reading.Sensor.Instance}/{reading.ReadingId}",
            HardwareType: hardware,
            HardwareName: hardware,
            SensorType: type.ToString(),
            SensorName: name,
            Category: category,
            Unit: reading.Unit,
            Value: value);
    }

    private static OptiSensorCategory Classify(SensorType type, string sensorName, string hardwareName)
    {
        if (type == SensorType.Fan) return OptiSensorCategory.Fan;
        if (type is SensorType.Power or SensorType.Volt or SensorType.Current) return OptiSensorCategory.Power;
        if (hardwareName.Contains("Battery", StringComparison.OrdinalIgnoreCase)) return OptiSensorCategory.Battery;
        if (hardwareName.Contains("GPU", StringComparison.OrdinalIgnoreCase)) return OptiSensorCategory.Gpu;
        if (hardwareName.Contains("CPU", StringComparison.OrdinalIgnoreCase) || hardwareName.Contains("Processor", StringComparison.OrdinalIgnoreCase)) return OptiSensorCategory.Cpu;
        return OptiSensorCategory.Other;
    }
}
