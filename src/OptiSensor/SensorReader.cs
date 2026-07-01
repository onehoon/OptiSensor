using LibreHardwareMonitor.Hardware;

namespace OptiSensor;

internal sealed class SensorReader : IDisposable
{
    private readonly Computer _computer = new()
    {
        IsGpuEnabled = true
    };

    public void Open()
    {
        _computer.Open();
    }

    public string? ReadOverlayLine()
    {
        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
                subHardware.Update();
        }

        var gpuSensors = _computer.Hardware
            .Where(IsGpu)
            .SelectMany(GetSensors)
            .ToArray();

        if (gpuSensors.Length == 0)
            return null;

        var temperature = PickSensor(gpuSensors, SensorType.Temperature, "GPU Core", "GPU Temperature", "Core");
        var power = PickSensor(gpuSensors, SensorType.Power, "GPU Package", "GPU Power", "Total");
        var load = PickSensor(gpuSensors, SensorType.Load, "GPU Core", "GPU Load", "Core");

        var parts = new List<string>();

        if (temperature?.Value is float tempValue)
            parts.Add($"GPU {tempValue:0}C");

        if (power?.Value is float powerValue)
            parts.Add($"{powerValue:0}W");

        if (load?.Value is float loadValue)
            parts.Add($"{loadValue:0}%");

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    public void Dispose()
    {
        _computer.Close();
    }

    private static bool IsGpu(IHardware hardware)
    {
        return hardware.HardwareType is HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia;
    }

    private static IEnumerable<ISensor> GetSensors(IHardware hardware)
    {
        foreach (var sensor in hardware.Sensors)
            yield return sensor;

        foreach (var subHardware in hardware.SubHardware)
        {
            foreach (var sensor in subHardware.Sensors)
                yield return sensor;
        }
    }

    private static ISensor? PickSensor(IEnumerable<ISensor> sensors, SensorType type, params string[] preferredNames)
    {
        var typedSensors = sensors
            .Where(sensor => sensor.SensorType == type && sensor.Value.HasValue)
            .ToArray();

        foreach (var preferredName in preferredNames)
        {
            var match = typedSensors.FirstOrDefault(sensor =>
                sensor.Name.Contains(preferredName, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
                return match;
        }

        return typedSensors.FirstOrDefault();
    }
}
