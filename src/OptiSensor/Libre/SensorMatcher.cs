using OptiSensor.Models;

namespace OptiSensor.Libre;

internal static class SensorMatcher
{
    public static DetectedSensorInfo? FindBestMatch(
        SelectedOverlaySensor selectedSensor,
        IReadOnlyCollection<DetectedSensorInfo> detectedSensors)
    {
        var exactId = detectedSensors.FirstOrDefault(sensor =>
            string.Equals(sensor.SensorId, selectedSensor.SensorId, StringComparison.OrdinalIgnoreCase));

        if (exactId is not null)
            return exactId;

        var exactFields = detectedSensors.FirstOrDefault(sensor =>
            string.Equals(sensor.HardwareType, selectedSensor.HardwareType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(sensor.SensorType, selectedSensor.SensorType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(sensor.SensorName, selectedSensor.SensorName, StringComparison.OrdinalIgnoreCase));

        if (exactFields is not null)
            return exactFields;

        var normalizedSelectedName = Normalize(selectedSensor.SensorName);
        if (!string.IsNullOrWhiteSpace(normalizedSelectedName))
        {
            var normalizedFields = detectedSensors.FirstOrDefault(sensor =>
            {
                var normalizedSensorName = Normalize(sensor.SensorName);
                return !string.IsNullOrWhiteSpace(normalizedSensorName) &&
                    string.Equals(sensor.HardwareType, selectedSensor.HardwareType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(sensor.SensorType, selectedSensor.SensorType, StringComparison.OrdinalIgnoreCase) &&
                    (normalizedSensorName.Contains(normalizedSelectedName, StringComparison.OrdinalIgnoreCase) ||
                     normalizedSelectedName.Contains(normalizedSensorName, StringComparison.OrdinalIgnoreCase));
            });

            if (normalizedFields is not null)
                return normalizedFields;
        }

        return detectedSensors.FirstOrDefault(sensor =>
            string.Equals(sensor.HardwareType, selectedSensor.HardwareType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(sensor.SensorType, selectedSensor.SensorType, StringComparison.OrdinalIgnoreCase) &&
            sensor.Category == selectedSensor.Category);
    }

    public static bool HasExactSensorIdMatch(SelectedOverlaySensor selectedSensor, DetectedSensorInfo detectedSensor)
    {
        return string.Equals(selectedSensor.SensorId, detectedSensor.SensorId, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray());
    }
}
