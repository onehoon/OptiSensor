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
        var normalizedFields = detectedSensors.FirstOrDefault(sensor =>
            string.Equals(sensor.HardwareType, selectedSensor.HardwareType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(sensor.SensorType, selectedSensor.SensorType, StringComparison.OrdinalIgnoreCase) &&
            Normalize(sensor.SensorName).Contains(normalizedSelectedName, StringComparison.OrdinalIgnoreCase));

        if (normalizedFields is not null)
            return normalizedFields;

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
