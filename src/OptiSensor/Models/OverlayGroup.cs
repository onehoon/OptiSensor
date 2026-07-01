namespace OptiSensor.Models;

internal sealed record OverlayGroup
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required int Order { get; set; }
    public required bool Enabled { get; set; }
    public required List<SelectedOverlaySensor> Sensors { get; set; }

    public OverlayGroup Copy()
    {
        return new OverlayGroup
        {
            Id = Id,
            Name = Name,
            Order = Order,
            Enabled = Enabled,
            Sensors = Sensors
                .OrderBy(sensor => sensor.Order)
                .Select(sensor => sensor.Copy())
                .ToList()
        };
    }
}
