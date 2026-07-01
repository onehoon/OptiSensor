namespace OptiSensor.Models;

internal sealed record SelectedOverlaySensor
{
    public required string SensorId { get; set; }
    public required string HardwareType { get; set; }
    public required string HardwareName { get; set; }
    public required string SensorType { get; set; }
    public required string SensorName { get; set; }
    public required OptiSensorCategory Category { get; set; }
    public required string DisplayName { get; set; }
    public required string Unit { get; set; }
    public required string Format { get; set; }
    public required int Order { get; set; }
    public required bool Enabled { get; set; }

    public SelectedOverlaySensor Copy()
    {
        return new SelectedOverlaySensor
        {
            SensorId = SensorId,
            HardwareType = HardwareType,
            HardwareName = HardwareName,
            SensorType = SensorType,
            SensorName = SensorName,
            Category = Category,
            DisplayName = DisplayName,
            Unit = Unit,
            Format = Format,
            Order = Order,
            Enabled = Enabled
        };
    }
}
