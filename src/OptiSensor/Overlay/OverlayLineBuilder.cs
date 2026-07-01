using OptiSensor.Libre;
using OptiSensor.Models;

namespace OptiSensor.Overlay;

internal sealed class OverlayLineBuilder
{
    public string? BuildDefaultLine(LibreSensorSnapshot snapshot)
    {
        var selectedSensors = CreateDefaultGpuSelections(snapshot).ToArray();
        return BuildLine(snapshot, selectedSensors);
    }

    public string? BuildLine(LibreSensorSnapshot snapshot, IReadOnlyCollection<SelectedOverlaySensor> selectedSensors)
    {
        var sensorById = snapshot.Sensors.ToDictionary(sensor => sensor.SensorId, StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();

        foreach (var selectedSensor in selectedSensors.Where(sensor => sensor.Enabled).OrderBy(sensor => sensor.Order))
        {
            if (!sensorById.TryGetValue(selectedSensor.SensorId, out var detectedSensor) ||
                detectedSensor.Value is not float value)
            {
                continue;
            }

            parts.Add(FormatSensor(selectedSensor, value));
        }

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static IEnumerable<SelectedOverlaySensor> CreateDefaultGpuSelections(LibreSensorSnapshot snapshot)
    {
        var gpuSensors = snapshot.Sensors
            .Where(sensor => SensorClassifier.IsGpuHardware(sensor.HardwareType))
            .ToArray();

        var temperature = PickSensor(gpuSensors, "Temperature", "GPU Core", "GPU Temperature", "Core");
        var power = PickSensor(gpuSensors, "Power", "GPU Package", "GPU Power", "Total");
        var load = PickSensor(gpuSensors, "Load", "GPU Core", "GPU Load", "Core");

        if (temperature is not null)
            yield return CreateSelection(temperature, "GPU", "{0:0}C", 0);

        if (power is not null)
            yield return CreateSelection(power, string.Empty, "{0:0}W", 1);

        if (load is not null)
            yield return CreateSelection(load, string.Empty, "{0:0}%", 2);
    }

    private static SelectedOverlaySensor CreateSelection(DetectedSensorInfo sensor, string displayName, string format, int order)
    {
        return new SelectedOverlaySensor
        {
            SensorId = sensor.SensorId,
            HardwareType = sensor.HardwareType,
            HardwareName = sensor.HardwareName,
            SensorType = sensor.SensorType,
            SensorName = sensor.SensorName,
            Category = sensor.Category,
            DisplayName = displayName,
            Unit = sensor.Unit,
            Format = format,
            Order = order,
            Enabled = true
        };
    }

    private static DetectedSensorInfo? PickSensor(IEnumerable<DetectedSensorInfo> sensors, string type, params string[] preferredNames)
    {
        var typedSensors = sensors
            .Where(sensor => string.Equals(sensor.SensorType, type, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var preferredName in preferredNames)
        {
            var match = typedSensors.FirstOrDefault(sensor =>
                sensor.SensorName.Contains(preferredName, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
                return match;
        }

        return typedSensors.FirstOrDefault();
    }

    private static string FormatSensor(SelectedOverlaySensor sensor, float value)
    {
        string formattedValue;
        try
        {
            var format = string.IsNullOrWhiteSpace(sensor.Format)
                ? SensorFormatDefaults.GetDefaultFormat(sensor.Unit)
                : sensor.Format;

            formattedValue = string.Format(CultureInfo.InvariantCulture, format, value);
        }
        catch (FormatException)
        {
            formattedValue = value.ToString("0", CultureInfo.InvariantCulture) + sensor.Unit;
        }

        return string.IsNullOrWhiteSpace(sensor.DisplayName)
            ? formattedValue
            : $"{sensor.DisplayName} {formattedValue}";
    }
}
