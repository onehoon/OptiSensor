namespace OptiSensor.Claw;

/// <summary>
/// The single concrete owner of the native Claw telemetry readers. Sampling and publishing are
/// independent: the sampling loop refreshes a source group on its own cadence (Core ~1000 ms,
/// Battery ~5000 ms) and recomposes <see cref="Latest"/>; the publish loop only reads
/// <see cref="Latest"/>. Retention rules:
/// <list type="bullet">
///   <item>not due -> retained values unchanged (the read simply does not run);</item>
///   <item>due, metric read produced a value -> that metric is replaced;</item>
///   <item>due, metric transiently unavailable -> the last successful value for that metric is kept;</item>
///   <item>a successful battery read replaces the whole battery snapshot, because a null
///   <c>RemainingMinutes</c> is meaningful after an AC/DC change and must clear an old estimate;</item>
///   <item>a fresh publishing session starts empty.</item>
/// </list>
/// Not started by the application from this type; <see cref="Publishing"/> drives it.
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
    /// second Core sample after the first to warm up. A metric is replaced only when this read
    /// produced a value for it; a transient miss keeps the last successful value.
    /// </summary>
    public void SampleCore()
    {
        // Retry readers that never came up. Once initialized they stay initialized, so this only
        // costs a check per tick in steady state.
        if (!_windowsUsage.Initialized)
            _windowsUsage.Initialize();
        if (!_igclGpu.Initialized)
            _igclGpu.Initialize();

        _usage = MergeUsage(_windowsUsage.Sample(), _usage);
        _ec = MergeEc(_msiEc.ReadSnapshot(), _ec);
        _gpu = MergeGpu(_igclGpu.Sample(), _gpu);
        Recompose();
    }

    /// <summary>Battery read (percent / on-battery / remaining time). Intended cadence ~5000 ms.</summary>
    public void SampleBattery()
    {
        // A successful read replaces the whole battery snapshot - a null RemainingMinutes is
        // semantic (fresh state after an AC/DC change) and must clear an old estimate. A failed
        // read (null) keeps the last-known battery state.
        if (WindowsPowerTelemetry.Read() is { } power)
            _power = power;

        Recompose();
    }

    // Core merge = last-successful-value per metric: a value from this read wins; a null field
    // (whole read failed, or that metric transiently unavailable) keeps the retained value. A
    // genuine numeric 0 is a value - nullable presence, not non-zero, is the validity signal.

    internal static WindowsUsageSnapshot? MergeUsage(WindowsUsageSnapshot? incoming, WindowsUsageSnapshot? retained)
    {
        if (incoming is null)
            return retained;

        return new WindowsUsageSnapshot(
            CpuUsagePercent: incoming.CpuUsagePercent ?? retained?.CpuUsagePercent,
            SystemMemoryUsedBytes: incoming.SystemMemoryUsedBytes ?? retained?.SystemMemoryUsedBytes,
            IntelGpuMemoryUsedBytes: incoming.IntelGpuMemoryUsedBytes ?? retained?.IntelGpuMemoryUsedBytes);
    }

    internal static MsiEcTelemetrySnapshot MergeEc(MsiEcTelemetrySnapshot incoming, MsiEcTelemetrySnapshot retained)
    {
        return new MsiEcTelemetrySnapshot(
            CpuTempC: incoming.CpuTempC ?? retained.CpuTempC,
            Fan1Rpm: incoming.Fan1Rpm ?? retained.Fan1Rpm,
            Fan2Rpm: incoming.Fan2Rpm ?? retained.Fan2Rpm,
            HudFanRpm: incoming.HudFanRpm ?? retained.HudFanRpm,
            CpuPackagePowerW: incoming.CpuPackagePowerW ?? retained.CpuPackagePowerW);
    }

    internal static IgclGpuTelemetrySnapshot? MergeGpu(IgclGpuTelemetrySnapshot? incoming, IgclGpuTelemetrySnapshot? retained)
    {
        if (incoming is null)
            return retained;

        return new IgclGpuTelemetrySnapshot(
            GpuUsagePercent: incoming.GpuUsagePercent ?? retained?.GpuUsagePercent,
            GpuClockMHz: incoming.GpuClockMHz ?? retained?.GpuClockMHz);
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
