using OptiSensor.Models;

namespace OptiSensor.Overlay;

internal static class SensorFormatDefaults
{
    public static string GetDefaultFormat(string unit)
    {
        return unit switch
        {
            "C" => "{0:0}C",
            "W" => "{0:0}W",
            "%" => "{0:0}%",
            "RPM" => "{0:0}RPM",
            _ => "{0:0}"
        };
    }

    public static string GetDefaultDisplayName(DetectedSensorInfo sensor)
    {
        return sensor.Category switch
        {
            OptiSensorCategory.Gpu => sensor.SensorType == "Temperature" ? "GPU" : string.Empty,
            OptiSensorCategory.Cpu => sensor.SensorType == "Temperature" ? "CPU" : string.Empty,
            OptiSensorCategory.Battery => "BAT",
            OptiSensorCategory.Fan => "FAN",
            _ => string.Empty
        };
    }
}
