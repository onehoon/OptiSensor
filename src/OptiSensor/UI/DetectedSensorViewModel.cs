using OptiSensor.Models;

namespace OptiSensor.UI;

internal sealed class DetectedSensorViewModel
{
    public DetectedSensorViewModel(DetectedSensorInfo sensor)
    {
        Sensor = sensor;
    }

    public DetectedSensorInfo Sensor { get; }
    public string SensorId => Sensor.SensorId;
    public string Category => Sensor.Category.ToString();
    public string HardwareName => Sensor.HardwareName;
    public string SensorName => Sensor.SensorName;
    public string SensorType => Sensor.SensorType;
    public string Unit => Sensor.Unit;
    public string ValueText => Sensor.Value is float value
        ? $"{value:0.#} {Sensor.Unit}"
        : string.Empty;
}
