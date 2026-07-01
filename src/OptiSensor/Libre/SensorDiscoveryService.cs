namespace OptiSensor.Libre;

internal sealed class SensorDiscoveryService
{
    private readonly LibreSensorReader _sensorReader;

    public SensorDiscoveryService(LibreSensorReader sensorReader)
    {
        _sensorReader = sensorReader;
    }

    public LibreSensorSnapshot Discover()
    {
        return _sensorReader.ReadSnapshot();
    }
}
