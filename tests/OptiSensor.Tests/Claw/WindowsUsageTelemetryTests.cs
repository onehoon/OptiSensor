using OptiSensor.Claw;
using Xunit;

namespace OptiSensor.Tests.Claw;

public class WindowsUsageTelemetryTests
{
    private const ulong Gib = 1024ul * 1024 * 1024;

    [Fact]
    public void UsedPhysicalMemory_TotalMinusAvailable()
    {
        Assert.Equal(20 * Gib, WindowsUsageTelemetryReader.UsedPhysicalMemory(32 * Gib, 12 * Gib));
    }

    [Fact]
    public void UsedPhysicalMemory_AvailableAboveTotalIsUnavailable()
    {
        Assert.Null(WindowsUsageTelemetryReader.UsedPhysicalMemory(12, 32));
    }

    [Fact]
    public void UsedPhysicalMemory_ZeroUsedIsValid()
    {
        Assert.Equal(0ul, WindowsUsageTelemetryReader.UsedPhysicalMemory(32 * Gib, 32 * Gib));
    }

    [Theory]
    [InlineData(33.0, 33.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(125.0, 100.0)]
    [InlineData(100.0, 100.0)]
    public void NormalizeUsagePercent_ValidAndClamped(double input, double expected)
    {
        Assert.Equal(expected, WindowsUsageTelemetryReader.NormalizeUsagePercent(input));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NormalizeUsagePercent_InvalidIsUnavailable(double input)
    {
        Assert.Null(WindowsUsageTelemetryReader.NormalizeUsagePercent(input));
    }
}
