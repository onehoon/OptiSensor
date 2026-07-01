namespace OptiSensor.Models;

internal sealed record SelectedOverlaySensor
{
    public required string SensorId { get; init; }
    public required string HardwareType { get; init; }
    public required string HardwareName { get; init; }
    public required string SensorType { get; init; }
    public required string SensorName { get; init; }
    public required OptiSensorCategory Category { get; init; }
    public required string DisplayName { get; init; }
    public required string Unit { get; init; }
    public required string Format { get; init; }
    public required int Order { get; init; }
    public required bool Enabled { get; init; }
}
