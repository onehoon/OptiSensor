using OptiSensor.Models;

namespace OptiSensor.Libre;

internal sealed record LibreSensorSnapshot(IReadOnlyList<DetectedSensorInfo> Sensors);
