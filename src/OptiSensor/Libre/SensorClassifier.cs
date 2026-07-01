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

        return TryClassifyGenericHardware(sensor, out category, out unit);
    }

    public static bool IsGpuHardware(string hardwareType)
    {
        return hardwareType.Contains("Gpu", StringComparison.OrdinalIgnoreCase);
    }

    public static void DescribeForDisplay(IHardware hardware, ISensor sensor, out OptiSensorCategory category, out string unit)
    {
        category = GetDisplayCategory(hardware, sensor);
        unit = GetUnit(sensor.SensorType);
    }

    private static bool TryClassifyGpu(ISensor sensor, out OptiSensorCategory category, out string unit)
    {
        switch (sensor.SensorType)
        {
            case SensorType.Temperature:
                category = OptiSensorCategory.Gpu;
                unit = "°C";
                return true;
            case SensorType.Load:
                category = OptiSensorCategory.Gpu;
                unit = "%";
                return true;
            case SensorType.Power:
                category = OptiSensorCategory.Power;
                unit = "W";
                return true;
            case SensorType.Fan:
                category = OptiSensorCategory.Fan;
                unit = "RPM";
                return true;
            default:
                category = default;
                unit = string.Empty;
                return false;
        }
    }

    private static bool TryClassifyCpu(ISensor sensor, out OptiSensorCategory category, out string unit)
    {
        switch (sensor.SensorType)
        {
            case SensorType.Temperature:
                category = OptiSensorCategory.Cpu;
                unit = "°C";
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
                category = OptiSensorCategory.Fan;
                unit = "RPM";
                return true;
            default:
                category = default;
                unit = string.Empty;
                return false;
        }
    }

    private static bool TryClassifyBattery(ISensor sensor, out OptiSensorCategory category, out string unit)
    {
        switch (sensor.SensorType)
        {
            case SensorType.Level:
            case SensorType.Temperature:
                category = OptiSensorCategory.Battery;
                unit = sensor.SensorType == SensorType.Temperature ? "°C" : "%";
                return true;
            case SensorType.Power:
                category = OptiSensorCategory.Power;
                unit = "W";
                return true;
            default:
                category = default;
                unit = string.Empty;
                return false;
        }
    }

    private static bool TryClassifyGenericHardware(ISensor sensor, out OptiSensorCategory category, out string unit)
    {
        switch (sensor.SensorType)
        {
            case SensorType.Fan:
                category = OptiSensorCategory.Fan;
                unit = "RPM";
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

    private static OptiSensorCategory GetDisplayCategory(IHardware hardware, ISensor sensor)
    {
        if (IsGpu(hardware))
            return OptiSensorCategory.Gpu;

        if (hardware.HardwareType is HardwareType.Cpu)
            return OptiSensorCategory.Cpu;

        if (hardware.HardwareType is HardwareType.Battery)
            return OptiSensorCategory.Battery;

        return sensor.SensorType switch
        {
            SensorType.Power => OptiSensorCategory.Power,
            SensorType.Fan => OptiSensorCategory.Fan,
            _ => OptiSensorCategory.Other
        };
    }

    private static string GetUnit(SensorType sensorType)
    {
        return sensorType switch
        {
            SensorType.Voltage => "V",
            SensorType.Current => "A",
            SensorType.Power => "W",
            SensorType.Clock => "MHz",
            SensorType.Temperature => "°C",
            SensorType.Load => "%",
            SensorType.Frequency => "Hz",
            SensorType.Fan => "RPM",
            SensorType.Flow => "L/h",
            SensorType.Control => "%",
            SensorType.Level => "%",
            SensorType.Factor => string.Empty,
            SensorType.Data => "GB",
            SensorType.SmallData => "MB",
            SensorType.Throughput => "MB/s",
            SensorType.TimeSpan => "s",
            SensorType.Energy => "Wh",
            SensorType.Noise => "dBA",
            SensorType.Conductivity => string.Empty,
            SensorType.Humidity => "%",
            _ => string.Empty
        };
    }
}
