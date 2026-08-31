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
        var sensorById = snapshot.Sensors
            .GroupBy(sensor => sensor.SensorId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
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

    public string? BuildLine(LibreSensorSnapshot snapshot, IReadOnlyCollection<OverlayGroup> overlayGroups)
    {
        var sensorById = snapshot.Sensors
            .GroupBy(sensor => sensor.SensorId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var groupParts = new List<string>();

        foreach (var group in overlayGroups.Where(group => group.Enabled).OrderBy(group => group.Order))
        {
            var sensorParts = new List<string>();
            foreach (var selectedSensor in group.Sensors.Where(sensor => sensor.Enabled).OrderBy(sensor => sensor.Order))
            {
                if (!sensorById.TryGetValue(selectedSensor.SensorId, out var detectedSensor) ||
                    detectedSensor.Value is not float value)
                {
                    continue;
                }

                sensorParts.Add(FormatSensor(selectedSensor, value));
            }

            if (sensorParts.Count == 0)
                continue;

            var groupText = string.Join(" ", sensorParts);
            if (!string.IsNullOrWhiteSpace(group.Name))
                groupText = $"{group.Name} {groupText}";

            groupParts.Add(groupText);
        }

        return groupParts.Count == 0 ? null : string.Join(" | ", groupParts);
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
            yield return CreateSelection(temperature, "GPU", "{0:0}°C", 0);

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

            formattedValue = NormalizeTemperatureSuffix(
                sensor,
                string.Format(CultureInfo.InvariantCulture, format, value));
        }
        catch (FormatException)
        {
            formattedValue = value.ToString("0", CultureInfo.InvariantCulture) + GetDisplayUnit(sensor);
        }

        return string.IsNullOrWhiteSpace(sensor.DisplayName)
            ? formattedValue
            : $"{sensor.DisplayName} {formattedValue}";
    }

    private static readonly char[] Digits = "0123456789".ToCharArray();

    private static string NormalizeTemperatureSuffix(SelectedOverlaySensor sensor, string formattedValue)
    {
        if (!IsTemperatureSensor(sensor) || formattedValue.EndsWith("°C", StringComparison.Ordinal))
            return formattedValue;

        // The saved format string can carry a garbled degree sign: Hwinfo.SharedMemory mis-decodes
        // HWiNFO's OS-ANSI char[] fields as Latin-1, so on CP949/CP932/... the degree sign arrives
        // as stray bytes (e.g. "42¡ÆC"). Coerce any temperature output that still ends in C to a
        // clean "<number>°C".
        var lastDigit = formattedValue.LastIndexOfAny(Digits);
        if (lastDigit < 0)
            return formattedValue;

        var tail = formattedValue[(lastDigit + 1)..];
        return tail.Length > 0 && (tail[^1] is 'C' or 'c')
            ? formattedValue[..(lastDigit + 1)] + "°C"
            : formattedValue;
    }

    private static string GetDisplayUnit(SelectedOverlaySensor sensor)
    {
        return IsTemperatureSensor(sensor) ? "°C" : sensor.Unit;
    }

    private static bool IsTemperatureSensor(SelectedOverlaySensor sensor)
    {
        // LibreHardwareMonitor exposes the bare LHM name; the HWiNFO reader exposes "SensorType" +
        // the Hwinfo.SharedMemory enum name (SensorType.Temp -> "SensorTypeTemp").
        return string.Equals(sensor.SensorType, "Temperature", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sensor.SensorType, "SensorTypeTemp", StringComparison.OrdinalIgnoreCase);
    }
}
