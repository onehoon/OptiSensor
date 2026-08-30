namespace OptiSensor.Claw;

/// <summary>
/// Hardware-validated MSI EC telemetry, following ClawHUD's production parsing contract
/// (<c>ClawHUD/docs/MSI_EC_HARDWARE_VALIDATED_PARSING.md</c>). <c>null</c> means the value
/// was unavailable for this sample; a real stopped fan / genuine 0 W read is a value, not null.
/// </summary>
internal sealed record MsiEcTelemetrySnapshot(
    int? CpuTempC,
    int? Fan1Rpm,
    int? Fan2Rpm,
    int? CpuPackagePowerW)
{
    public static readonly MsiEcTelemetrySnapshot Empty = new(null, null, null, null);
}
