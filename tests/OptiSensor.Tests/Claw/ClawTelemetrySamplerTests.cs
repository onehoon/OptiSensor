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
        var ec = new MsiEcTelemetrySnapshot(CpuTempC: 67, Fan1Rpm: 3000, Fan2Rpm: 4000, HudFanRpm: 3540, CpuPackagePowerW: 18);
        var gpu = new IgclGpuTelemetrySnapshot(GpuUsagePercent: 98, GpuClockMHz: 2300);

        var snapshot = ClawTelemetrySampler.Compose(usage, power, ec, gpu);

        Assert.Equal(36, snapshot.CpuUsagePercent);
        Assert.Equal(67, snapshot.CpuTemperatureC);
        Assert.Equal(18, snapshot.CpuPackagePowerW);
        Assert.Equal(98, snapshot.GpuUsagePercent);
        Assert.Equal(2300, snapshot.GpuClockMHz);
        Assert.Equal(100ul, snapshot.SystemMemoryUsedBytes);
        Assert.Equal(200ul, snapshot.GpuMemoryUsedBytes);
        Assert.Equal(3540, snapshot.FanRpm); // EC's already-selected HUD fan, not Fan1/Fan2
        Assert.Equal(72, snapshot.BatteryPercent);
        Assert.Equal(true, snapshot.OnBattery);
        Assert.Equal(150, snapshot.RemainingMinutes);
    }

    [Fact]
    public void Compose_UnavailableSourceOnlyRemovesItsOwnFields()
    {
        var ec = new MsiEcTelemetrySnapshot(CpuTempC: 67, Fan1Rpm: null, Fan2Rpm: null, HudFanRpm: 3540, CpuPackagePowerW: 18);

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
        Assert.Equal(3540, snapshot.FanRpm);
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
        var usage = ClawTelemetrySampler.MergeUsage(new WindowsUsageSnapshot(50, 100, 200), null);
        var ec = ClawTelemetrySampler.MergeEc(new MsiEcTelemetrySnapshot(67, 3000, 3100, 3050, 18), MsiEcTelemetrySnapshot.Empty);
        var gpu = ClawTelemetrySampler.MergeGpu(new IgclGpuTelemetrySnapshot(98, 2300), null);

        // A due Core read where every reader missed / returned null this cycle.
        usage = ClawTelemetrySampler.MergeUsage(null, usage);
        ec = ClawTelemetrySampler.MergeEc(MsiEcTelemetrySnapshot.Empty, ec);
        gpu = ClawTelemetrySampler.MergeGpu(null, gpu);

        var snapshot = ClawTelemetrySampler.Compose(usage, power: null, ec: ec, gpu: gpu);
        Assert.Equal(50, snapshot.CpuUsagePercent);
        Assert.Equal(100ul, snapshot.SystemMemoryUsedBytes);
        Assert.Equal(67, snapshot.CpuTemperatureC);
        Assert.Equal(18, snapshot.CpuPackagePowerW);
        Assert.Equal(3050, snapshot.FanRpm);
        Assert.Equal(98, snapshot.GpuUsagePercent);
        Assert.Equal(2300, snapshot.GpuClockMHz);

        // A later valid read updates each metric; a genuine 0 W replaces.
        ec = ClawTelemetrySampler.MergeEc(new MsiEcTelemetrySnapshot(70, null, null, null, 0), ec);
        Assert.Equal(70, ec.CpuTempC);
        Assert.Equal(3050, ec.HudFanRpm);        // still kept (this read had no fan value)
        Assert.Equal(0, ec.CpuPackagePowerW);    // genuine 0 W replaces
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
