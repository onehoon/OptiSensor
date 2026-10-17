using LibreHardwareMonitor.Hardware;

namespace OptiSensor.Libre;

internal static class LibreSensorValueNormalizer
{
    private const float BytesPerMegabyte = 1024f * 1024f;
    private const float BytesPerGigabyte = BytesPerMegabyte * 1024f;

    public static float? Normalize(SensorType sensorType, float? value)
    {
        if (value is not float rawValue)
            return null;

        return sensorType switch
        {
            SensorType.Data => rawValue / BytesPerGigabyte,
            SensorType.Throughput => rawValue / BytesPerMegabyte,
            _ => rawValue
        };
    }
}
