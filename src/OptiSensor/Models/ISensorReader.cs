using OptiSensor.Libre;

namespace OptiSensor.Models;

internal interface ISensorReader : IDisposable
{
    void Open();
    LibreSensorSnapshot ReadSnapshot(bool includeAllSensors = false, IReadOnlyCollection<OptiSensorCategory>? includedCategories = null, bool fastStart = false);
}
