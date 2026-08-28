namespace OptiSensor.Claw;

/// <summary>
/// The single concrete owner of the merged native Claw telemetry readers. Sampling and
/// publishing are independent: the sampling loop refreshes a source group on its own cadence
/// (Core ~1000 ms, Battery ~5000 ms) and recomposes <see cref="Latest"/>; the publish loop only
/// reads <see cref="Latest"/>. A source that is not due keeps its last sampled value - a
/// publish-only tick never turns a retained value into <c>null</c>. Not started by the
/// application from this type; <see cref="Publishing"/> drives it.
/// </summary>
internal sealed class ClawTelemetrySampler : IDisposable
{
    private static readonly ClawTelemetrySnapshot EmptySnapshot =
        new(null, null, null, null, null, null, null, null, null, null, null);

    private readonly WindowsUsageTelemetryReader _windowsUsage = new();
    private readonly MsiEcTelemetryReader _msiEc = new();
    private readonly IgclGpuTelemetryReader _igclGpu = new();

    private WindowsUsageSnapshot? _usage;
    private WindowsPowerSnapshot? _power;
    private MsiEcTelemetrySnapshot _ec = MsiEcTelemetrySnapshot.Empty;
    private IgclGpuTelemetrySnapshot? _gpu;
    private ClawTelemetrySnapshot _latest = EmptySnapshot;

    /// <summary>
    /// The most recently composed snapshot, holding each source's last sampled value. Safe to
    /// read from the publish loop while the sampling loop mutates the retained sources.
    /// </summary>
    public ClawTelemetrySnapshot Latest => Volatile.Read(ref _latest);

    /// <summary>Initializes the readers that require it. Their own init/retry policies are unchanged.</summary>
    public void Initialize()
    {
        _windowsUsage.Initialize();
        _igclGpu.Initialize();
    }

    /// <summary>
    /// Core read: MSI EC (CPU temp / TDP / fan) + Windows CPU usage / RAM / Intel GPU memory +
    /// IGCL GPU usage / clock. Intended cadence ~1000 ms. Windows/IGCL rate counters need a
    /// second Core sample after the first to warm up.
    /// </summary>
    public void SampleCore()
    {
        _usage = _windowsUsage.Sample();
        _ec = _msiEc.ReadSnapshot();
        _gpu = _igclGpu.Sample();
        Recompose();
    }

    /// <summary>Battery read (percent / on-battery / remaining time). Intended cadence ~5000 ms.</summary>
    public void SampleBattery()
    {
        _power = WindowsPowerTelemetry.Read();
        Recompose();
    }

    private void Recompose() =>
        Volatile.Write(ref _latest, Compose(_usage, _power, _ec, _gpu));

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
