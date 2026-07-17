using System.Diagnostics;
using Hwinfo.SharedMemory;
using OptiSensor.Libre;
using OptiSensor.Models;

namespace OptiSensor.HWiNFO;

internal sealed class HwInfoSensorReader : ISensorReader
{
    private readonly SharedMemoryReader _reader = new(1000);

    public void Open()
    {
        // SharedMemoryReader opens the mapping on each read so HWiNFO can restart independently.
    }

    public LibreSensorSnapshot ReadSnapshot(
        bool includeAllSensors = false,
        IReadOnlyCollection<OptiSensorCategory>? includedCategories = null,
        bool fastStart = false)
    {
        var stopwatch = Stopwatch.StartNew();
        var readings = _reader.ReadLocal();
        var included = includedCategories is null ? null : includedCategories.ToHashSet();
        var sensors = readings
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

    private static DetectedSensorInfo Map(SensorReading reading)
    {
        var type = Get(reading, "SensorTypeUsage");
        var name = Get(reading, "SensorNameUser");
        if (string.IsNullOrWhiteSpace(name)) name = Get(reading, "SensorNameOrig");
        var hardware = Get(reading, "SensorSection");
        var sensorId = Get(reading, "SensorId");
        var instance = Get(reading, "SensorInstance");
        var unit = Get(reading, "Unit");
        var value = double.TryParse(Get(reading, "Value"), out var parsed) ? (float?)parsed : null;
        var category = Classify(type, name, hardware);
        return new DetectedSensorInfo(
            SensorId: $"hwinfo/{sensorId}/{instance}",
            HardwareType: hardware,
            HardwareName: hardware,
            SensorType: type,
            SensorName: name,
            Category: category,
            Unit: unit,
            Value: value);
    }

    private static string Get(SensorReading reading, string propertyName) => reading.GetType().GetProperty(propertyName)?.GetValue(reading)?.ToString() ?? string.Empty;

    private static OptiSensorCategory Classify(string type, string sensorName, string hardwareName)
    {
        if (hardwareName.Contains("GPU", StringComparison.OrdinalIgnoreCase)) return OptiSensorCategory.Gpu;
        if (hardwareName.Contains("CPU", StringComparison.OrdinalIgnoreCase) || hardwareName.Contains("Processor", StringComparison.OrdinalIgnoreCase)) return OptiSensorCategory.Cpu;
        var typeName = type;
        if (typeName.Contains("Fan", StringComparison.OrdinalIgnoreCase)) return OptiSensorCategory.Fan;
        if (typeName.Contains("Power", StringComparison.OrdinalIgnoreCase) || typeName.Contains("Volt", StringComparison.OrdinalIgnoreCase) || typeName.Contains("Current", StringComparison.OrdinalIgnoreCase)) return OptiSensorCategory.Power;
        if (typeName.Contains("Temp", StringComparison.OrdinalIgnoreCase) && sensorName.Contains("battery", StringComparison.OrdinalIgnoreCase)) return OptiSensorCategory.Battery;
        return OptiSensorCategory.Other;
    }
}
