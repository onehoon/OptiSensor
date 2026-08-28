namespace OptiSensor.Claw;

/// <summary>
/// The single concrete owner that samples the already-merged native Claw telemetry readers and
/// composes one <see cref="ClawTelemetrySnapshot"/>. Not started by the application yet - HWiNFO
/// remains the production publisher. Each source is sampled independently: an unavailable source
/// only removes its own fields.
/// </summary>
internal sealed class ClawTelemetrySampler : IDisposable
{
    private readonly WindowsUsageTelemetryReader _windowsUsage = new();
    private readonly MsiEcTelemetryReader _msiEc = new();
    private readonly IgclGpuTelemetryReader _igclGpu = new();

    /// <summary>Initializes the readers that require it. Their own init/retry policies are unchanged.</summary>
    public void Initialize()
    {
        _windowsUsage.Initialize();
        _igclGpu.Initialize();
    }

    public ClawTelemetrySnapshot Sample()
    {
        return Compose(
            _windowsUsage.Sample(),
            WindowsPowerTelemetry.Read(),
            _msiEc.ReadSnapshot(),
            _igclGpu.Sample());
    }

    /// <summary>Pure mapping from the source snapshots to the composed snapshot (one authority per field).</summary>
    internal static ClawTelemetrySnapshot Compose(
        WindowsUsageSnapshot? usage,
        WindowsPowerSnapshot? power,
        MsiEcTelemetrySnapshot ec,
        IgclGpuTelemetrySnapshot? gpu)
    {
        return new ClawTelemetrySnapshot(
            CpuUsagePercent: usage?.CpuUsagePercent,
            CpuTemperatureC: ec.CpuTempC,
            CpuPackagePowerW: ec.CpuPackagePowerW,
            GpuUsagePercent: gpu?.GpuUsagePercent,
            GpuClockMHz: gpu?.GpuClockMHz,
            SystemMemoryUsedBytes: usage?.SystemMemoryUsedBytes,
            GpuMemoryUsedBytes: usage?.IntelGpuMemoryUsedBytes,
            FanRpm: ec.HudFanRpm,
            BatteryPercent: power?.BatteryPercent,
            OnBattery: power?.OnBattery,
            RemainingMinutes: power?.RemainingMinutes);
    }

    public void Dispose()
    {
        _windowsUsage.Dispose();
        _igclGpu.Dispose();
    }
}
