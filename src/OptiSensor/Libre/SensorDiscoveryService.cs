using OptiSensor.HWiNFO;
using OptiSensor.Models;

namespace OptiSensor.Libre;

internal sealed class SensorDiscoveryService : IDisposable
{
    private readonly LibreSensorReader? _libreReader;
    private readonly HwInfoSensorReader? _hwInfoReader;
    private bool _opened;
    public SensorDiscoveryService() : this(SensorSourceKind.Libre) { }
    public SensorDiscoveryService(LibreSensorReader reader) => _libreReader = reader;
    public SensorDiscoveryService(SensorSourceKind source)
    {
        if (source == SensorSourceKind.HwInfo) _hwInfoReader = new HwInfoSensorReader();
        else _libreReader = new LibreSensorReader();
    }
    public LibreSensorSnapshot Discover(IReadOnlyCollection<OptiSensorCategory>? includedCategories = null, bool fastStart = false)
    {
        EnsureOpen();
        return _libreReader?.ReadSnapshot(true, includedCategories, fastStart)
            ?? _hwInfoReader!.ReadSnapshot(true, includedCategories, fastStart);
    }
    public void Dispose() { _libreReader?.Dispose(); _hwInfoReader?.Dispose(); }
    private void EnsureOpen()
    {
        if (_opened) return;
        if (_libreReader is not null) _libreReader.Open(); else _hwInfoReader!.Open();
        _opened = true;
    }
}
