using OptiSensor.Claw;
using Xunit;

namespace OptiSensor.Tests.Claw;

public class ClawTelemetryFormatterTests
{
    private const ulong Gib = 1024ul * 1024 * 1024;

    private static ClawTelemetrySnapshot Snap(
        double? cpuUsage = null, int? cpuTemp = null, int? tdp = null,
        double? gpuUsage = null, double? gpuClock = null,
        ulong? ram = null, ulong? vram = null, int? fan = null,
        int? battery = null, bool? onBattery = null, int? remaining = null)
        => new(cpuUsage, cpuTemp, tdp, gpuUsage, gpuClock, ram, vram, fan, battery, onBattery, remaining);

    [Fact]
    public void Format_FullSnapshotMatchesClawHudLine()
    {
        var snapshot = Snap(
            cpuUsage: 36, cpuTemp: 67, tdp: 18,
            gpuUsage: 98, gpuClock: 2300,
            ram: 20 * Gib, vram: (ulong)(9.4 * Gib), fan: 3540,
            battery: 72, onBattery: true, remaining: 150);

        Assert.Equal(
            "CPU 36% 67°C | GPU 98% 2300MHz | TDP 18W | RAM 20.0GB | VRAM 9.4GB | FAN 3540RPM | BAT 72% 2.5h",
            ClawTelemetryFormatter.Format(snapshot));
    }

    [Fact]
    public void Format_AllUnavailableIsEmptyString()
    {
        Assert.Equal(string.Empty, ClawTelemetryFormatter.Format(Snap()));
    }

    [Theory]
    [InlineData(36.0, null, "CPU 36%")]
    [InlineData(null, 67, "CPU 67°C")]
    [InlineData(36.0, 67, "CPU 36% 67°C")]
    public void Format_CpuPartialValues(double? usage, int? temp, string expected)
    {
        Assert.Equal(expected, ClawTelemetryFormatter.Format(Snap(cpuUsage: usage, cpuTemp: temp)));
    }

    [Theory]
    [InlineData(98.0, null, "GPU 98%")]
    [InlineData(null, 2300.0, "GPU 2300MHz")]
    [InlineData(98.0, 2300.0, "GPU 98% 2300MHz")]
    public void Format_GpuPartialValues(double? usage, double? clock, string expected)
    {
        Assert.Equal(expected, ClawTelemetryFormatter.Format(Snap(gpuUsage: usage, gpuClock: clock)));
    }

    [Fact]
    public void Format_UnavailableSegmentIsOmittedWithoutDoubledSeparator()
    {
        var snapshot = Snap(cpuUsage: 36, ram: 20 * Gib); // GPU/TDP/VRAM/FAN/BAT all unavailable

        Assert.Equal("CPU 36% | RAM 20.0GB", ClawTelemetryFormatter.Format(snapshot));
    }

    [Fact]
    public void Format_GenuineZeroValuesAreKept()
    {
        var snapshot = Snap(
            cpuUsage: 0, tdp: 0, gpuUsage: 0,
            ram: 0, vram: 0, fan: 0, battery: 0);

        Assert.Equal(
            "CPU 0% | GPU 0% | TDP 0W | RAM 0.0GB | VRAM 0.0GB | FAN 0RPM | BAT 0%",
            ClawTelemetryFormatter.Format(snapshot));
    }

    [Theory]
    [InlineData(20.0, "RAM 20.0GB")]
    [InlineData(8.4, "RAM 8.4GB")]
    public void Format_RamUsesBinaryGiBWithOneDecimal(double gib, string expected)
    {
        Assert.Equal(expected, ClawTelemetryFormatter.Format(Snap(ram: (ulong)(gib * Gib))));
    }

    [Fact]
    public void Format_VramUsesBinaryGiBWithOneDecimal()
    {
        Assert.Equal("VRAM 9.4GB", ClawTelemetryFormatter.Format(Snap(vram: (ulong)(9.4 * Gib))));
    }

    [Theory]
    [InlineData(150, true, "BAT 72% 2.5h")]
    [InlineData(120, true, "BAT 72% 2h")]
    [InlineData(90, true, "BAT 72% 1.5h")]
    [InlineData(60, true, "BAT 72% 1h")]
    [InlineData(45, true, "BAT 72% 45m")]
    [InlineData(150, false, "BAT 72%")]
    public void Format_BatteryRemainingTimeOnlyWhileOnBattery(int remaining, bool onBattery, string expected)
    {
        Assert.Equal(expected, ClawTelemetryFormatter.Format(Snap(battery: 72, onBattery: onBattery, remaining: remaining)));
    }

    [Fact]
    public void Format_BatteryWithoutRemainingHasNoTime()
    {
        Assert.Equal("BAT 72%", ClawTelemetryFormatter.Format(Snap(battery: 72, onBattery: true)));
    }

    [Fact]
    public void Format_SegmentOrderIsFixedRegardlessOfAvailability()
    {
        // Only BAT and CPU present; CPU must still come first.
        Assert.Equal("CPU 36% | BAT 72%", ClawTelemetryFormatter.Format(Snap(cpuUsage: 36, battery: 72)));
    }

    [Theory]
    [InlineData(36.5, "CPU 37%")]      // std::lround rounds .5 away from zero (banker's would give 36)
    [InlineData(35.5, "CPU 36%")]
    public void Format_UsageRoundingMatchesClawHud(double usage, string expected)
    {
        Assert.Equal(expected, ClawTelemetryFormatter.Format(Snap(cpuUsage: usage)));
    }

    [Fact]
    public void Format_ClockRoundingMatchesClawHud()
    {
        Assert.Equal("GPU 2301MHz", ClawTelemetryFormatter.Format(Snap(gpuClock: 2300.5)));
    }
}
