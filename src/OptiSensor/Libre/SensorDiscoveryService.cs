using OptiSensor.Models;

namespace OptiSensor.Libre;

internal sealed class SensorDiscoveryService : IDisposable
{
    private readonly LibreSensorReader _sensorReader;
    private bool _opened;

    public SensorDiscoveryService()
        : this(new LibreSensorReader())
    {
    }

    public SensorDiscoveryService(LibreSensorReader sensorReader)
    {
        _sensorReader = sensorReader;
    }

    public LibreSensorSnapshot Discover(IReadOnlyCollection<OptiSensorCategory>? includedCategories = null, bool fastStart = false)
    {
        EnsureOpen();
        return _sensorReader.ReadSnapshot(includeAllSensors: true, includedCategories: includedCategories, fastStart: fastStart);
    }

    public void Dispose()
    {
        _sensorReader.Dispose();
    }

    private void EnsureOpen()
    {
        if (_opened)
            return;

        _sensorReader.Open();
        _opened = true;
    }
}
