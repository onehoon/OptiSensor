using OptiSensor.Models;

namespace OptiSensor.Settings;

internal sealed class SensorSourceProfile
{
    public List<SelectedOverlaySensor> SelectedSensors { get; set; } = [];
    public List<OverlayGroup> OverlayGroups { get; set; } = [];
    public Dictionary<OptiSensorCategory, bool> SensorCategoryFilters { get; set; } = [];
}
