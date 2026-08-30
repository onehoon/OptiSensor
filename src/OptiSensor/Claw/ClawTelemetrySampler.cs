namespace OptiSensor.Claw;

/// <summary>
/// The single concrete owner of the native Claw telemetry readers. Sampling and publishing are
/// independent: the sampling loop refreshes a source group on its own cadence (Core ~1000 ms,
/// Battery ~5000 ms) and recomposes <see cref="Latest"/>; the publish loop only reads
/// <see cref="Latest"/>. Retention rules:
/// <list type="bullet">
///   <item>not due -> retained values unchanged (the read simply does not run);</item>
///   <item>due, metric read produced a value -> that metric is replaced;</item>
///   <item>due, metric transiently unavailable -> the last successful value for that metric is
///   kept, but only through a bounded number of consecutive misses (EC / IGCL fields clear after
///   3) so a stale value never lingers indefinitely;</item>
///   <item>an IGCL provider that keeps failing is Reset() after 3 consecutive provider-call
///   failures, so the normal <see cref="SampleCore"/> re-init path can bring it back;</item>
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

    // Consecutive missing samples an EC / IGCL field survives before its stale value is cleared.
    private const int EcTelemetryMissingThreshold = 3;
    private const int IgclTelemetryMissingThreshold = 3;

    private readonly WindowsUsageTelemetryReader _windowsUsage = new();
    private readonly MsiEcTelemetryReader _msiEc = new();
    private readonly IgclGpuTelemetryReader _igclGpu = new();

    private WindowsUsageSnapshot? _usage;
    private WindowsPowerSnapshot? _power;
    private MsiEcTelemetrySnapshot _ec = MsiEcTelemetrySnapshot.Empty;
    private IgclGpuTelemetrySnapshot? _gpu;
    private ClawTelemetrySnapshot _latest = EmptySnapshot;

    // Independent consecutive-miss counters for the bounded-retention EC / IGCL fields.
    private int _ecCpuTempMisses;
    private int _ecFan1Misses;
    private int _ecFan2Misses;
    private int _ecTdpMisses;
    private int _gpuUsageMisses;
    private int _gpuClockMisses;

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
        MergeEc(_msiEc.ReadSnapshot());
        MergeGpu(_igclGpu.Sample());
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

    /// <summary>
    /// Bounded per-field EC retention. Each source field (CPU temp, Fan 1, Fan 2, TDP) survives up
    /// to <see cref="EcTelemetryMissingThreshold"/> - 1 consecutive misses and is then cleared; a
    /// fresh value recovers it immediately. The single overlay FAN value is not stored here - it is
    /// derived from the retained Fan 1 + Fan 2 pair in <see cref="Compose"/>. A genuine numeric 0
    /// (stopped fan, 0 W) is a value, not a miss.
    /// </summary>
    internal MsiEcTelemetrySnapshot MergeEc(MsiEcTelemetrySnapshot incoming)
    {
        var cpuTempC = UpdateRetainedField(_ec.CpuTempC, incoming.CpuTempC, ref _ecCpuTempMisses, EcTelemetryMissingThreshold);
        var fan1Rpm = UpdateRetainedField(_ec.Fan1Rpm, incoming.Fan1Rpm, ref _ecFan1Misses, EcTelemetryMissingThreshold);
        var fan2Rpm = UpdateRetainedField(_ec.Fan2Rpm, incoming.Fan2Rpm, ref _ecFan2Misses, EcTelemetryMissingThreshold);
        var cpuPackagePowerW = UpdateRetainedField(_ec.CpuPackagePowerW, incoming.CpuPackagePowerW, ref _ecTdpMisses, EcTelemetryMissingThreshold);

        _ec = new MsiEcTelemetrySnapshot(
            CpuTempC: cpuTempC,
            Fan1Rpm: fan1Rpm,
            Fan2Rpm: fan2Rpm,
            CpuPackagePowerW: cpuPackagePowerW);
        return _ec;
    }

    /// <summary>
    /// The single overlay FAN value: mean of both retained fans, otherwise the one available fan,
    /// otherwise unavailable. Integer arithmetic; a genuine 0 RPM is a value.
    /// </summary>
    internal static int? ComposeFanRpm(int? fan1Rpm, int? fan2Rpm)
    {
        if (fan1Rpm is int fan1 && fan2Rpm is int fan2)
            return (fan1 + fan2) / 2;

        return fan1Rpm ?? fan2Rpm;
    }

    /// <summary>Bounded per-field IGCL retention for GPU usage and GPU clock, independently.</summary>
    internal IgclGpuTelemetrySnapshot? MergeGpu(IgclGpuTelemetrySnapshot? incoming)
    {
        var gpuUsagePercent = UpdateRetainedField(_gpu?.GpuUsagePercent, incoming?.GpuUsagePercent, ref _gpuUsageMisses, IgclTelemetryMissingThreshold);
        var gpuClockMHz = UpdateRetainedField(_gpu?.GpuClockMHz, incoming?.GpuClockMHz, ref _gpuClockMisses, IgclTelemetryMissingThreshold);

        _gpu = gpuUsagePercent is null && gpuClockMHz is null
            ? null
            : new IgclGpuTelemetrySnapshot(gpuUsagePercent, gpuClockMHz);
        return _gpu;
    }

    /// <summary>
    /// One field of bounded last-successful-value retention. A fresh <paramref name="incoming"/>
    /// value replaces <paramref name="retained"/> and clears the miss streak. A miss keeps the
    /// retained value until <paramref name="missingCount"/> reaches <paramref name="threshold"/>,
    /// then the field is cleared and the streak reset. Once cleared, further misses do nothing.
    /// A genuine numeric 0 is a value.
    /// </summary>
    internal static T? UpdateRetainedField<T>(T? retained, T? incoming, ref int missingCount, int threshold)
        where T : struct
    {
        if (incoming is { } value)
        {
            missingCount = 0;
            return value;
        }

        if (retained is null)
            return null;

        if (++missingCount < threshold)
            return retained;

        missingCount = 0;
        return null;
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
            FanRpm: ComposeFanRpm(ec.Fan1Rpm, ec.Fan2Rpm),
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
