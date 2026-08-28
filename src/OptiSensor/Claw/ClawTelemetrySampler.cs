namespace OptiSensor.Claw;

/// <summary>
/// The single concrete owner of the merged native Claw telemetry readers. Sampling and
/// publishing are independent: the sampling loop refreshes a source group on its own cadence
/// (Core ~1000 ms, Battery ~5000 ms) and recomposes <see cref="Latest"/>; the publish loop only
/// reads <see cref="Latest"/>. The retention boundary is the <b>sampling cadence</b>: when a
/// source is not due it is simply not read and <see cref="Latest"/> stays unchanged; when it is
/// due it is read and its snapshot replaced with that read's result, <b>including null /
/// unavailable fields</b> - a metric a due read reports unavailable becomes unavailable rather
/// than immortal stale data. A fresh publishing session starts empty. Not started by the
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

    /// <summary>
    /// First attempt to bring up the stateful readers. A reader that is not ready yet (e.g. PDH /
    /// IGCL during early Windows boot) is retried by <see cref="SampleCore"/> on the Core cadence,
    /// so a transient startup miss never becomes a permanently missing metric.
    /// </summary>
    public void Initialize()
    {
        _windowsUsage.Initialize();
        _igclGpu.Initialize();
    }

    /// <summary>
    /// Core read: MSI EC (CPU temp / TDP / fan) + Windows CPU usage / RAM / Intel GPU memory +
    /// IGCL GPU usage / clock. Intended cadence ~1000 ms. Windows/IGCL rate counters need a
    /// second Core sample after the first to warm up. The read replaces each source snapshot
    /// wholesale, including null fields - a metric this read reports unavailable becomes
    /// unavailable, not indefinitely stale.
    /// </summary>
    public void SampleCore()
    {
        // Retry readers that never came up. Once initialized they stay initialized, so this only
        // costs a check per tick in steady state.
        if (!_windowsUsage.Initialized)
            _windowsUsage.Initialize();
        if (!_igclGpu.Initialized)
            _igclGpu.Initialize();

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
