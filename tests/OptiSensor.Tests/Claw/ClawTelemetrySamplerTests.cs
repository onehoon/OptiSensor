using OptiSensor.Claw;
using Xunit;

namespace OptiSensor.Tests.Claw;

public class ClawTelemetrySamplerTests
{
    [Fact]
    public void Compose_MapsEachSourceToItsOwnedField()
    {
        var usage = new WindowsUsageSnapshot(CpuUsagePercent: 36, SystemMemoryUsedBytes: 100, IntelGpuMemoryUsedBytes: 200);
        var power = new WindowsPowerSnapshot(BatteryPercent: 72, RemainingMinutes: 150, OnBattery: true);
        var ec = new MsiEcTelemetrySnapshot(CpuTempC: 67, Fan1Rpm: 3000, Fan2Rpm: 4000, CpuPackagePowerW: 18);
        var gpu = new IgclGpuTelemetrySnapshot(GpuUsagePercent: 98, GpuClockMHz: 2300);

        var snapshot = ClawTelemetrySampler.Compose(usage, power, ec, gpu);

        Assert.Equal(36, snapshot.CpuUsagePercent);
        Assert.Equal(67, snapshot.CpuTemperatureC);
        Assert.Equal(18, snapshot.CpuPackagePowerW);
        Assert.Equal(98, snapshot.GpuUsagePercent);
        Assert.Equal(2300, snapshot.GpuClockMHz);
        Assert.Equal(100ul, snapshot.SystemMemoryUsedBytes);
        Assert.Equal(200ul, snapshot.GpuMemoryUsedBytes);
        Assert.Equal(3500, snapshot.FanRpm); // derived once from Fan1/Fan2 mean at composition
        Assert.Equal(72, snapshot.BatteryPercent);
        Assert.Equal(true, snapshot.OnBattery);
        Assert.Equal(150, snapshot.RemainingMinutes);
    }

    [Fact]
    public void Compose_UnavailableSourceOnlyRemovesItsOwnFields()
    {
        var ec = new MsiEcTelemetrySnapshot(CpuTempC: 67, Fan1Rpm: 3520, Fan2Rpm: 3560, CpuPackagePowerW: 18);

        // IGCL + Windows usage + Windows power all unavailable this sample.
        var snapshot = ClawTelemetrySampler.Compose(usage: null, power: null, ec: ec, gpu: null);

        Assert.Null(snapshot.GpuUsagePercent);
        Assert.Null(snapshot.GpuClockMHz);
        Assert.Null(snapshot.CpuUsagePercent);
        Assert.Null(snapshot.SystemMemoryUsedBytes);
        Assert.Null(snapshot.GpuMemoryUsedBytes);
        Assert.Null(snapshot.BatteryPercent);
        Assert.Null(snapshot.OnBattery);
        Assert.Null(snapshot.RemainingMinutes);

        // EC fields survive.
        Assert.Equal(67, snapshot.CpuTemperatureC);
        Assert.Equal(18, snapshot.CpuPackagePowerW);
        Assert.Equal(3540, snapshot.FanRpm); // (3520 + 3560) / 2
    }

    // ---- retention: not due = keep; due + transient miss = keep; due + value = replace ----

    [Fact]
    public void Latest_PublishOnlyReadsKeepLastSample()
    {
        using var sampler = new ClawTelemetrySampler();

        // Repeated reads with no sampling in between return the same retained snapshot.
        var before = sampler.Latest;
        Assert.Same(before, sampler.Latest);
        Assert.Equal(
            new ClawTelemetrySnapshot(null, null, null, null, null, null, null, null, null, null, null),
            before);
    }

    [Fact]
    public void CoreTransientUnavailable_KeepsLastSuccessfulMetrics()
    {
        using var sampler = new ClawTelemetrySampler();

        var usage = ClawTelemetrySampler.MergeUsage(new WindowsUsageSnapshot(50, 100, 200), null);
        sampler.MergeEc(new MsiEcTelemetrySnapshot(67, 3000, 3100, 18));
        sampler.MergeGpu(new IgclGpuTelemetrySnapshot(98, 2300));

        // A due Core read where every reader missed / returned null this cycle.
        usage = ClawTelemetrySampler.MergeUsage(null, usage);
        var ec = sampler.MergeEc(MsiEcTelemetrySnapshot.Empty);
        var gpu = sampler.MergeGpu(null);

        var snapshot = ClawTelemetrySampler.Compose(usage, power: null, ec: ec, gpu: gpu);
        Assert.Equal(50, snapshot.CpuUsagePercent);
        Assert.Equal(100ul, snapshot.SystemMemoryUsedBytes);
        Assert.Equal(67, snapshot.CpuTemperatureC);
        Assert.Equal(18, snapshot.CpuPackagePowerW);
        Assert.Equal(3050, snapshot.FanRpm); // derived mean of retained Fan1/Fan2
        Assert.Equal(98, snapshot.GpuUsagePercent);
        Assert.Equal(2300, snapshot.GpuClockMHz);

        // A later valid read updates each metric; a genuine 0 W replaces.
        ec = sampler.MergeEc(new MsiEcTelemetrySnapshot(70, null, null, 0));
        Assert.Equal(70, ec.CpuTempC);
        Assert.Equal(3050, ClawTelemetrySampler.ComposeFanRpm(ec.Fan1Rpm, ec.Fan2Rpm)); // fans still retained (this read had none)
        Assert.Equal(0, ec.CpuPackagePowerW);    // genuine 0 W replaces
    }

    // ---- bounded per-field retention: UpdateRetainedField ----------------

    [Fact]
    public void UpdateRetainedField_RetainsThroughTwoMissesThenClearsThenRecoversImmediately()
    {
        var misses = 0;
        Assert.Equal(67, ClawTelemetrySampler.UpdateRetainedField<int>(null, 67, ref misses, 3));
        Assert.Equal(67, ClawTelemetrySampler.UpdateRetainedField<int>(67, null, ref misses, 3)); // miss 1
        Assert.Equal(67, ClawTelemetrySampler.UpdateRetainedField<int>(67, null, ref misses, 3)); // miss 2
        Assert.Null(ClawTelemetrySampler.UpdateRetainedField<int>(67, null, ref misses, 3));      // miss 3 -> clear
        Assert.Equal(0, misses);
        Assert.Equal(68, ClawTelemetrySampler.UpdateRetainedField<int>(null, 68, ref misses, 3)); // immediate recovery
        Assert.Equal(0, misses);
    }

    [Fact]
    public void UpdateRetainedField_GenuineZeroIsAValueAndResetsTheMissStreak()
    {
        var misses = 2;
        Assert.Equal(0, ClawTelemetrySampler.UpdateRetainedField<int>(22, 0, ref misses, 3));
        Assert.Equal(0, misses);
    }

    // ---- EC bounded retention through the sampler -----------------------

    [Fact]
    public void MergeEc_CpuTempRetainsThroughTwoMissesThenClearsThenRecovers()
    {
        using var s = new ClawTelemetrySampler();

        Assert.Equal(67, s.MergeEc(new MsiEcTelemetrySnapshot(67, null, null, null)).CpuTempC);
        Assert.Equal(67, s.MergeEc(MsiEcTelemetrySnapshot.Empty).CpuTempC); // miss 1
        Assert.Equal(67, s.MergeEc(MsiEcTelemetrySnapshot.Empty).CpuTempC); // miss 2
        Assert.Null(s.MergeEc(MsiEcTelemetrySnapshot.Empty).CpuTempC);      // miss 3 -> clear
        Assert.Equal(68, s.MergeEc(new MsiEcTelemetrySnapshot(68, null, null, null)).CpuTempC);
    }

    [Fact]
    public void MergeEc_FanFieldsRetainIndependentlyAndFinalFanRpmIsDerivedAtComposition()
    {
        using var s = new ClawTelemetrySampler();

        static int? FinalFan(MsiEcTelemetrySnapshot ec) =>
            ClawTelemetrySampler.ComposeFanRpm(ec.Fan1Rpm, ec.Fan2Rpm);

        var r1 = s.MergeEc(new MsiEcTelemetrySnapshot(null, 3200, 3500, null));
        Assert.Equal(3200, r1.Fan1Rpm);
        Assert.Equal(3500, r1.Fan2Rpm);
        Assert.Equal(3350, FinalFan(r1)); // mean of the current pair

        // Fan1 misses, Fan2 has a fresh value.
        var r2 = s.MergeEc(new MsiEcTelemetrySnapshot(null, null, 3600, null));
        Assert.Equal(3200, r2.Fan1Rpm); // retained
        Assert.Equal(3600, r2.Fan2Rpm);
        Assert.Equal(3400, FinalFan(r2)); // (retained 3200 + fresh 3600) / 2

        s.MergeEc(new MsiEcTelemetrySnapshot(null, null, 3600, null)); // Fan1 miss 2
        var r4 = s.MergeEc(new MsiEcTelemetrySnapshot(null, null, 3600, null)); // Fan1 miss 3 -> clear
        Assert.Null(r4.Fan1Rpm);
        Assert.Equal(3600, r4.Fan2Rpm);
        Assert.Equal(3600, FinalFan(r4)); // Fan2 only
    }

    [Theory]
    [InlineData(3200, 3600, 3400)] // both fans -> integer mean
    [InlineData(3200, null, 3200)] // Fan1 only
    [InlineData(null, 3600, 3600)] // Fan2 only
    [InlineData(null, null, null)] // neither
    [InlineData(0, 0, 0)]          // genuine stopped-fan zero
    public void ComposeFanRpm_SingleFanPresentationPolicy(int? fan1, int? fan2, int? expected)
    {
        Assert.Equal(expected, ClawTelemetrySampler.ComposeFanRpm(fan1, fan2));
    }

    [Fact]
    public void MergeEc_ZeroTdpIsValidAndRetainedThroughAMiss()
    {
        using var s = new ClawTelemetrySampler();

        s.MergeEc(new MsiEcTelemetrySnapshot(null, null, null, 22));
        Assert.Equal(0, s.MergeEc(new MsiEcTelemetrySnapshot(null, null, null, 0)).CpuPackagePowerW); // genuine 0 W
        Assert.Equal(0, s.MergeEc(MsiEcTelemetrySnapshot.Empty).CpuPackagePowerW); // retained through the miss
    }

    // ---- IGCL bounded retention through the sampler --------------------

    [Fact]
    public void MergeGpu_UsageAndClockRetainIndependently()
    {
        using var s = new ClawTelemetrySampler();

        var g1 = s.MergeGpu(new IgclGpuTelemetrySnapshot(98, 2300));
        Assert.Equal(98, g1!.GpuUsagePercent);
        Assert.Equal(2300, g1.GpuClockMHz);

        // Usage misses, clock is fresh.
        var g2 = s.MergeGpu(new IgclGpuTelemetrySnapshot(null, 2250));
        Assert.Equal(98, g2!.GpuUsagePercent);
        Assert.Equal(2250, g2.GpuClockMHz);

        s.MergeGpu(new IgclGpuTelemetrySnapshot(null, 2250)); // usage miss 2
        var g4 = s.MergeGpu(new IgclGpuTelemetrySnapshot(null, 2250)); // usage miss 3 -> clear
        Assert.Null(g4!.GpuUsagePercent);
        Assert.Equal(2250, g4.GpuClockMHz); // clock unaffected

        var g5 = s.MergeGpu(new IgclGpuTelemetrySnapshot(70, 2250));
        Assert.Equal(70, g5!.GpuUsagePercent); // immediate recovery
    }

    [Fact]
    public void MergeGpu_NullSampleCountsAsAMissForBothFields()
    {
        using var s = new ClawTelemetrySampler();

        s.MergeGpu(new IgclGpuTelemetrySnapshot(98, 2300));
        Assert.Equal(98, s.MergeGpu(null)!.GpuUsagePercent);  // miss 1
        Assert.Equal(98, s.MergeGpu(null)!.GpuUsagePercent);  // miss 2
        Assert.Null(s.MergeGpu(null));                        // miss 3 -> both cleared -> whole snapshot null
    }

    [Fact]
    public void BatterySuccessfulRead_CanClearOldRemainingEstimate()
    {
        // A successful battery read replaces the whole snapshot, so a fresh OnBattery sample with
        // no remaining-time estimate yet cannot show the previous discharge's "2.5h".
        var afterAcDcTransition = new WindowsPowerSnapshot(BatteryPercent: 71, RemainingMinutes: null, OnBattery: true);

        var composed = ClawTelemetrySampler.Compose(
            usage: null, power: afterAcDcTransition, ec: MsiEcTelemetrySnapshot.Empty, gpu: null);

        Assert.Equal(71, composed.BatteryPercent);
        Assert.Equal(true, composed.OnBattery);
        Assert.Null(composed.RemainingMinutes);
    }

    [Fact]
    public void SampleCore_SelfInitializesReadersThatAreNotYetInitialized()
    {
        using var sampler = new ClawTelemetrySampler();

        // Deliberately skip Initialize(): a startup init miss must not be permanent, so SampleCore
        // brings up any reader that is not ready yet on its own 1 s cadence.
        sampler.SampleCore(); // initializes + priming sample
        sampler.SampleCore(); // warmed sample

        // Windows RAM (GlobalMemoryStatusEx behind the PDH-gated reader) is available on any
        // Windows runner once that reader is up - proof SampleCore initialized it.
        Assert.NotNull(sampler.Latest.SystemMemoryUsedBytes);
    }

}
