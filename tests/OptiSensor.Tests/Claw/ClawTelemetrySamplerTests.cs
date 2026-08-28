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

    // ---- retention boundary = sampling cadence --------------------------
    // not due  -> Latest unchanged (SampleCore/SampleBattery simply not called)
    // due read -> replace that source snapshot wholesale, including null/unavailable fields

    [Fact]
    public void Latest_PublishOnlyReadsKeepLastSample()
    {
        using var sampler = new ClawTelemetrySampler();

        // Repeated reads with no sampling in between return the same retained snapshot; a
        // publish tick never re-samples or gaps a value.
        var before = sampler.Latest;
        Assert.Same(before, sampler.Latest);
        Assert.Equal(
            new ClawTelemetrySnapshot(null, null, null, null, null, null, null, null, null, null, null),
            before);
    }

    [Fact]
    public void Compose_DueReadUnavailableClearsThatSourceFields()
    {
        var previous = ClawTelemetrySampler.Compose(
            usage: null, power: null, ec: MsiEcTelemetrySnapshot.Empty,
            gpu: new IgclGpuTelemetrySnapshot(98, 2300));
        Assert.Equal(98, previous.GpuUsagePercent);
        Assert.Equal(2300, previous.GpuClockMHz);

        // Next scheduled Core read: IGCL failed -> gpu snapshot is null -> GPU segment is gone,
        // not the old 98% / 2300 MHz.
        var afterFailedDueRead = ClawTelemetrySampler.Compose(
            usage: null, power: null, ec: MsiEcTelemetrySnapshot.Empty, gpu: null);
        Assert.Null(afterFailedDueRead.GpuUsagePercent);
        Assert.Null(afterFailedDueRead.GpuClockMHz);
    }

    [Fact]
    public void Compose_BatteryDcEstimateDoesNotSurviveIntoANewBatterySessionWithoutAFreshEstimate()
    {
        // An earlier discharge had RemainingMinutes = 150. After AC then unplugging again, Windows
        // reports OnBattery = true but no new estimate yet: the read snapshot itself carries the
        // null, so the formatter can never show the previous "2.5h".
        var fresh = new WindowsPowerSnapshot(BatteryPercent: 70, RemainingMinutes: null, OnBattery: true);
        Assert.Null(fresh.RemainingMinutes);

        var composed = ClawTelemetrySampler.Compose(
            usage: null, power: fresh, ec: MsiEcTelemetrySnapshot.Empty, gpu: null);
        Assert.Equal(70, composed.BatteryPercent);
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
