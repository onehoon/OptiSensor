using OptiSensor.Models;

namespace OptiSensor.Overlay;

internal static class SensorFormatDefaults
{
    public static string GetDefaultFormat(string unit)
    {
        return unit switch
        {
            "V" => "{0:0.##}V",
            "A" => "{0:0.##}A",
            "C" => "{0:0}°C",
            "°C" => "{0:0}°C",
            "W" => "{0:0}W",
            "%" => "{0:0}%",
            "RPM" => "{0:#,0}RPM",
            "MHz" => "{0:#,0.#}MHz",
            "Hz" => "{0:0.#}Hz",
            "kHz" => "{0:0.#}kHz",
            "GHz" => "{0:0.#}GHz",
            "MB" => "{0:0}MB",
            "GB" => "{0:0.#}GB",
            "MB/s" => "{0:0.#}MB/s",
            "L/h" => "{0:0.#}L/h",
            "s" => "{0:0.#}s",
            "Wh" => "{0:0.#}Wh",
            "dBA" => "{0:0.#}dBA",
            _ => string.IsNullOrWhiteSpace(unit) ? "{0:0}" : $"{{0:0}}{unit}"
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
