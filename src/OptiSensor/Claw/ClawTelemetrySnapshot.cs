namespace OptiSensor.Claw;

/// <summary>
/// One sample of the native Claw telemetry sources. Each field is owned by exactly one merged
/// reader (see <see cref="ClawTelemetrySampler"/>); <c>null</c> means that field was unavailable
/// for this sample. A field being present is decided by nullable presence, never by the value
/// being non-zero - a genuine 0% / 0W / 0RPM is a real reading.
/// </summary>
internal sealed record ClawTelemetrySnapshot(
    double? CpuUsagePercent,
    int? CpuTemperatureC,
    int? CpuPackagePowerW,
    double? GpuUsagePercent,
    double? GpuClockMHz,
    ulong? SystemMemoryUsedBytes,
    ulong? GpuMemoryUsedBytes,
    int? FanRpm,
    int? BatteryPercent,
    bool? OnBattery,
    int? RemainingMinutes);
