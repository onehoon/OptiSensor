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

    [Fact]
    public void Compose_ScheduledUnavailableReadClearsThatSource()
    {
        // A scheduled read is authoritative: when IGCL / Windows power report unavailable, the
        // composed snapshot drops those fields rather than showing the previous value forever.
        var withGpuAndBattery = ClawTelemetrySampler.Compose(
            usage: new WindowsUsageSnapshot(50, 100, 200),
            power: new WindowsPowerSnapshot(72, 150, true),
            ec: new MsiEcTelemetrySnapshot(67, null, null, 3540, 18),
            gpu: new IgclGpuTelemetrySnapshot(98, 2300));
        Assert.Equal(98, withGpuAndBattery.GpuUsagePercent);
        Assert.Equal(72, withGpuAndBattery.BatteryPercent);

        var afterLoss = ClawTelemetrySampler.Compose(
            usage: new WindowsUsageSnapshot(50, 100, 200),
            power: null,   // GetSystemPowerStatus failed this cycle
            ec: new MsiEcTelemetrySnapshot(67, null, null, 3540, 18),
            gpu: null);    // ctlPowerTelemetryGetV2 failed this cycle
        Assert.Null(afterLoss.GpuUsagePercent);
        Assert.Null(afterLoss.GpuClockMHz);
        Assert.Null(afterLoss.BatteryPercent);
        // Sources that still read fine are unaffected.
        Assert.Equal(50, afterLoss.CpuUsagePercent);
        Assert.Equal(67, afterLoss.CpuTemperatureC);
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

    [Fact]
    public void SampleCore_SourceRetriesUninitializedReadersEachTick()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "OptiSensor", "Claw", "ClawTelemetrySampler.cs"));
        var sampleCore = source[source.IndexOf("public void SampleCore()", StringComparison.Ordinal)..];
        sampleCore = sampleCore[..sampleCore.IndexOf("Recompose();", StringComparison.Ordinal)];

        Assert.Contains("if (!_windowsUsage.Initialized)", sampleCore);
        Assert.Contains("_windowsUsage.Initialize();", sampleCore);
        Assert.Contains("if (!_igclGpu.Initialized)", sampleCore);
        Assert.Contains("_igclGpu.Initialize();", sampleCore);
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFilePath = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFilePath)!, "..", "..", ".."));
}
