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

    public LibreSensorSnapshot Discover()
    {
        EnsureOpen();
        return _sensorReader.ReadSnapshot(includeAllSensors: true);
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
