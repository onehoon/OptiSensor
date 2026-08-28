using OptiSensor.HWiNFO;
using OptiSensor.Models;

namespace OptiSensor.Libre;

internal sealed class SensorDiscoveryService : IDisposable
{
    private readonly HwInfoSensorReader _hwInfoReader = new();
    private bool _opened;
    private bool _disposed;

    public LibreSensorSnapshot Discover(IReadOnlyCollection<OptiSensorCategory>? includedCategories = null, bool fastStart = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureOpen();
        return _hwInfoReader.ReadSnapshot(true, includedCategories, fastStart);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hwInfoReader.Dispose();
    }

    private void EnsureOpen()
    {
        if (_opened) return;
        _hwInfoReader.Open();
        _opened = true;
    }
}
