using LibreHardwareMonitor.Hardware;
using OptiSensor.Models;

namespace OptiSensor.Libre;

internal sealed class LibreSensorReader : IDisposable
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsBatteryEnabled = true,
        IsControllerEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsNetworkEnabled = true,
        IsStorageEnabled = true
    };

    public void Open()
    {
        _computer.Open();
    }

    public LibreSensorSnapshot ReadSnapshot(bool includeAllSensors = false)
    {
        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
                subHardware.Update();
        }

        var sensors = _computer.Hardware
            .SelectMany(hardware => GetDetectedSensors(hardware, includeAllSensors))
            .Where(sensor => sensor.Value.HasValue)
            .ToArray();

        return new LibreSensorSnapshot(sensors);
    }

    public void Dispose()
    {
        _computer.Close();
    }

    private static IEnumerable<DetectedSensorInfo> GetDetectedSensors(IHardware hardware, bool includeAllSensors)
    {
        foreach (var detectedSensor in GetDetectedSensors(hardware, hardware, includeAllSensors))
            yield return detectedSensor;

        foreach (var subHardware in hardware.SubHardware)
        {
            foreach (var detectedSensor in GetDetectedSensors(hardware, subHardware, includeAllSensors))
                yield return detectedSensor;
        }
    }

    private static IEnumerable<DetectedSensorInfo> GetDetectedSensors(IHardware rootHardware, IHardware sensorHardware, bool includeAllSensors)
    {
        foreach (var sensor in sensorHardware.Sensors)
        {
            var isSupportedOverlaySensor = SensorClassifier.TryClassify(rootHardware, sensor, out var category, out var unit);
            if (!isSupportedOverlaySensor && !includeAllSensors)
                continue;

            if (!isSupportedOverlaySensor)
                SensorClassifier.DescribeForDisplay(rootHardware, sensor, out category, out unit);

            yield return new DetectedSensorInfo(
                SensorId: BuildSensorId(rootHardware, sensorHardware, sensor),
                HardwareType: rootHardware.HardwareType.ToString(),
                HardwareName: rootHardware.Name,
                SensorType: sensor.SensorType.ToString(),
                SensorName: sensor.Name,
                Category: category,
                Unit: unit,
                Value: sensor.Value);
        }
    }

    private static string BuildSensorId(IHardware rootHardware, IHardware sensorHardware, ISensor sensor)
    {
        return string.Join(
            "/",
            rootHardware.HardwareType,
            Sanitize(rootHardware.Name),
            Sanitize(sensorHardware.Name),
            sensor.SensorType,
            Sanitize(sensor.Name),
            sensor.Index);
    }

    private static string Sanitize(string value)
    {
        return value.Replace('/', '_').Replace('\\', '_');
    }
}
