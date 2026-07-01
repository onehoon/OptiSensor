using OptiSensor.Models;

namespace OptiSensor.Libre;

internal sealed record LibreSensorSnapshot(
	IReadOnlyList<DetectedSensorInfo> Sensors,
	LibreReadMetrics Metrics);

internal sealed record LibreReadMetrics(
	int HardwareCount,
	int UpdatedHardwareCount,
	int SensorCount,
	int FallbackSensorIdCount,
	int DuplicateSensorIdCount,
	long UpdateMs,
	long ProjectionMs,
	bool FastStartApplied);
