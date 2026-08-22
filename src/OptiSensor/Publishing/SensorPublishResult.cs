using OptiSensor.Models;

namespace OptiSensor.Publishing;

internal sealed record SensorPublishResult(
    string? OverlayLine,
    IReadOnlyList<DetectedSensorInfo> Sensors,
    int DetectedSensorCount,
    int EnabledSelectedSensorCount,
    int TotalSelectedSensorCount);
