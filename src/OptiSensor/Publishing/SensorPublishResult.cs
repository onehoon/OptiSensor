namespace OptiSensor.Publishing;

internal sealed record SensorPublishResult(
    string? OverlayLine,
    int DetectedSensorCount,
    int SelectedSensorCount);
