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
    /// second Core sample after the first to warm up. A field is only replaced when this read
    /// produced a new valid value - an unavailable field keeps its last-known value.
    /// </summary>
    public void SampleCore()
    {
        _usage = MergeUsage(_windowsUsage.Sample(), _usage);
        _ec = MergeEc(_msiEc.ReadSnapshot(), _ec);
        _gpu = MergeGpu(_igclGpu.Sample(), _gpu);
        Recompose();
    }

    /// <summary>Battery read (percent / on-battery / remaining time). Intended cadence ~5000 ms.</summary>
    public void SampleBattery()
    {
        // WindowsPowerSnapshot fields move together, so a successful read replaces it wholesale;
        // a failed GetSystemPowerStatus (null) keeps the last-known battery state.
        if (WindowsPowerTelemetry.Read() is { } power)
            _power = power;

        Recompose();
    }

    // Merge = per-field last-known-value: a new valid reading wins, otherwise the retained value
    // is kept. A genuine numeric 0 is a valid reading and is not treated as "missing".

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
