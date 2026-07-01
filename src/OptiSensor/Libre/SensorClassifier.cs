using LibreHardwareMonitor.Hardware;
using OptiSensor.Models;

namespace OptiSensor.Libre;

internal static class SensorClassifier
{
    public static bool TryClassify(IHardware hardware, ISensor sensor, out OptiSensorCategory category, out string unit)
    {
        category = default;
        unit = string.Empty;

        if (hardware.HardwareType is HardwareType.Storage or HardwareType.Network or HardwareType.Memory)
            return false;

        if (IsGpu(hardware))
            return TryClassifyGpu(sensor, out category, out unit);

        if (hardware.HardwareType is HardwareType.Cpu)
            return TryClassifyCpu(sensor, out category, out unit);

        if (hardware.HardwareType is HardwareType.Battery)
            return TryClassifyBattery(sensor, out category, out unit);

        return TryClassifyGeneric(sensor, out category, out unit);
    }

    public static bool IsGpuHardware(string hardwareType)
    {
        return hardwareType.Contains("Gpu", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryClassifyGpu(ISensor sensor, out OptiSensorCategory category, out string unit)
    {
        if (TryClassifyGeneric(sensor, out category, out unit))
        {
            if (sensor.SensorType is SensorType.Temperature or SensorType.Load)
                category = OptiSensorCategory.Gpu;

            return true;
        }

        return false;
    }

    private static bool TryClassifyCpu(ISensor sensor, out OptiSensorCategory category, out string unit)
    {
        if (TryClassifyGeneric(sensor, out category, out unit))
        {
            if (sensor.SensorType is SensorType.Temperature or SensorType.Load)
                category = OptiSensorCategory.Cpu;

            return true;
        }

        return false;
    }

    private static bool TryClassifyBattery(ISensor sensor, out OptiSensorCategory category, out string unit)
    {
        if (TryClassifyGeneric(sensor, out category, out unit))
        {
            category = OptiSensorCategory.Battery;
            return true;
        }

        return false;
    }

    private static bool TryClassifyGeneric(ISensor sensor, out OptiSensorCategory category, out string unit)
    {
        switch (sensor.SensorType)
        {
            case SensorType.Temperature:
                category = OptiSensorCategory.Cpu;
                unit = "C";
                return true;
            case SensorType.Load:
                category = OptiSensorCategory.Cpu;
                unit = "%";
                return true;
            case SensorType.Power:
                category = OptiSensorCategory.Power;
                unit = "W";
                return true;
            case SensorType.Fan:
            case SensorType.Control:
                category = OptiSensorCategory.Fan;
                unit = sensor.SensorType == SensorType.Fan ? "RPM" : "%";
                return true;
            case SensorType.Level:
                category = OptiSensorCategory.Battery;
                unit = "%";
                return true;
            default:
                category = default;
                unit = string.Empty;
                return false;
        }
    }

    private static bool IsGpu(IHardware hardware)
    {
        return hardware.HardwareType is HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia;
    }
}
