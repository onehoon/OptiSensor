namespace OptiSensor.Models;

internal sealed record DetectedSensorInfo(
    string SensorId,
    string HardwareType,
    string HardwareName,
    string SensorType,
    string SensorName,
    OptiSensorCategory Category,
    string Unit,
    float? Value);
