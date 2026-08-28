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

    [Fact]
    public void Latest_IsARetainedGetterThatReadingDoesNotMutate()
    {
        using var sampler = new ClawTelemetrySampler();

        var first = sampler.Latest;
        var second = sampler.Latest;

        // A publish-only tick just reads Latest; repeated reads with no sampling in between
        // return the same retained snapshot rather than a freshly re-sampled (possibly gapped) one.
        Assert.Same(first, second);
        Assert.Equal(
            new ClawTelemetrySnapshot(null, null, null, null, null, null, null, null, null, null, null),
            first);
    }

    // ---- per-field last-known-value merge (a valid value survives a later unavailable read) --

    [Fact]
    public void MergeUsage_KeepsRetainedFieldWhenNewReadingIsUnavailable()
    {
        var retained = new WindowsUsageSnapshot(CpuUsagePercent: 40, SystemMemoryUsedBytes: 100, IntelGpuMemoryUsedBytes: 200);

        // New Core read: CPU present, RAM present-but-different, VRAM unavailable this cycle.
        var merged = ClawTelemetrySampler.MergeUsage(
            new WindowsUsageSnapshot(CpuUsagePercent: 55, SystemMemoryUsedBytes: 150, IntelGpuMemoryUsedBytes: null),
            retained);

        Assert.Equal(55, merged!.CpuUsagePercent);
        Assert.Equal(150ul, merged.SystemMemoryUsedBytes);
        Assert.Equal(200ul, merged.IntelGpuMemoryUsedBytes); // retained
    }

    [Fact]
    public void MergeUsage_NullReadingKeepsWholeRetainedSnapshot()
    {
        var retained = new WindowsUsageSnapshot(CpuUsagePercent: 40, SystemMemoryUsedBytes: 100, IntelGpuMemoryUsedBytes: 200);
        Assert.Same(retained, ClawTelemetrySampler.MergeUsage(null, retained));
    }

    [Fact]
    public void MergeEc_KeepsRetainedFieldsForUnavailableReadings()
    {
        var retained = new MsiEcTelemetrySnapshot(CpuTempC: 67, Fan1Rpm: 3000, Fan2Rpm: 3100, HudFanRpm: 3050, CpuPackagePowerW: 18);

        var merged = ClawTelemetrySampler.MergeEc(
            new MsiEcTelemetrySnapshot(CpuTempC: 70, Fan1Rpm: null, Fan2Rpm: null, HudFanRpm: null, CpuPackagePowerW: 0),
            retained);

        Assert.Equal(70, merged.CpuTempC);
        Assert.Equal(3050, merged.HudFanRpm);   // retained
        Assert.Equal(0, merged.CpuPackagePowerW); // genuine 0 W is a valid reading, not "missing"
    }

    [Fact]
    public void MergeGpu_KeepsRetainedUsageWhenNewSampleIsWarmingUp()
    {
        var retained = new IgclGpuTelemetrySnapshot(GpuUsagePercent: 90, GpuClockMHz: 2200);

        // IGCL re-primed: clock present, usage null while it rebuilds its delta baseline.
        var merged = ClawTelemetrySampler.MergeGpu(
            new IgclGpuTelemetrySnapshot(GpuUsagePercent: null, GpuClockMHz: 2400),
            retained);

        Assert.Equal(90, merged!.GpuUsagePercent); // retained
        Assert.Equal(2400, merged.GpuClockMHz);
    }
}
