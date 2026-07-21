using OptiSensor.Libre;
using OptiSensor.Models;

namespace OptiSensor.Tests.TestData;

/// <summary>Deterministic fixture builders for tests. No production algorithms are re-implemented here.</summary>
internal static class SensorFixture
{
    public const string GpuHardwareType = "GpuNvidia";
    public const string CpuHardwareType = "CpuGeneric";

    public static DetectedSensorInfo CreateDetectedSensor(
        string sensorId,
        string sensorType,
        string sensorName,
        float? value,
        string hardwareType = GpuHardwareType,
        string hardwareName = "GPU 0",
        OptiSensorCategory category = OptiSensorCategory.Gpu,
        string unit = "")
    {
        return new DetectedSensorInfo(sensorId, hardwareType, hardwareName, sensorType, sensorName, category, unit, value);
    }

    public static SelectedOverlaySensor CreateSelectedSensor(
        string sensorId,
        string displayName,
        string format,
        int order,
        bool enabled = true,
        string sensorType = "Temperature",
        OptiSensorCategory category = OptiSensorCategory.Gpu,
        string hardwareType = GpuHardwareType,
        string unit = "")
    {
        return new SelectedOverlaySensor
        {
            SensorId = sensorId,
            HardwareType = hardwareType,
            HardwareName = "GPU 0",
            SensorType = sensorType,
            SensorName = sensorId,
            Category = category,
            DisplayName = displayName,
            Unit = unit,
            Format = format,
            Order = order,
            Enabled = enabled
        };
    }

    public static OverlayGroup CreateGroup(string id, string name, int order, bool enabled, params SelectedOverlaySensor[] sensors)
    {
        return new OverlayGroup
        {
            Id = id,
            Name = name,
            Order = order,
            Enabled = enabled,
            Sensors = sensors.ToList()
        };
    }

    public static LibreSensorSnapshot CreateSnapshot(params DetectedSensorInfo[] sensors)
    {
        return new LibreSensorSnapshot(sensors, new LibreReadMetrics(0, 0, sensors.Length, 0, 0, 0, 0, false));
    }
}
