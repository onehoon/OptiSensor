using OptiSensor.Claw;
using Xunit;

namespace OptiSensor.Tests.Claw;

public class WindowsUsageTelemetryTests
{
    private const ulong Gib = 1024ul * 1024 * 1024;

    [Fact]
    public void Initialized_IsFalseBeforeInitializeAndTrueAfterASuccessfulInitialize()
    {
        using var reader = new WindowsUsageTelemetryReader();
        Assert.False(reader.Initialized);

        // The "% Processor Utility" counter exists on any supported Windows, so Initialize succeeds.
        Assert.True(reader.Initialize());
        Assert.True(reader.Initialized);
    }

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

    // ---- Intel GPU memory: LUID parsing ------------------------------------

    [Fact]
    public void ParseGpuMemoryInstanceLuid_ZeroPaddedComponents()
    {
        var luid = WindowsUsageTelemetryReader.ParseGpuMemoryInstanceLuid("luid_0x00000000_0x00123456_phys_0");

        Assert.NotNull(luid);
        Assert.Equal(0, luid!.Value.HighPart);
        Assert.Equal(0x00123456u, luid.Value.LowPart);
    }

    [Theory]
    [InlineData("luid_0x0_0x123456_phys_0")]
    [InlineData("luid_0x00000000_0x123456_phys_0")]
    [InlineData("luid_0x00000000_0x00123456_phys_0")]
    public void IsIntelGpuMemoryCounterInstance_MatchesCompactAndPaddedForms(string instance)
    {
        Assert.True(WindowsUsageTelemetryReader.IsIntelGpuMemoryCounterInstance(instance, new Luid(0x00123456u, 0)));
    }

    [Fact]
    public void IsIntelGpuMemoryCounterInstance_MatchesFullIntelLuid()
    {
        Assert.True(WindowsUsageTelemetryReader.IsIntelGpuMemoryCounterInstance(
            "luid_0x9abcdef0_0x12345678_phys_0",
            new Luid(0x12345678u, unchecked((int)0x9abcdef0))));
    }

    [Fact]
    public void IsIntelGpuMemoryCounterInstance_MatchesFullLuidWithSharedLeadingDigits()
    {
        Assert.True(WindowsUsageTelemetryReader.IsIntelGpuMemoryCounterInstance(
            "luid_0x00000000_0x00013245_phys_0", new Luid(0x00013245u, 0)));
    }

    [Theory]
    [InlineData("luid_0x00000000_0x0001368a_phys_0")] // different full LUID, shared leading digits
    [InlineData("luid_0x0_0x1234567_phys_0")]         // prefix collision (wanted 0x123456)
    [InlineData("luid_0x9abcdef0_0x12345678_other_0")] // non-physical suffix
    [InlineData("foo")]
    [InlineData("luid_xxx")]
    [InlineData("luid_0xZZ_0x1234_phys_0")]
    [InlineData("luid_0x0_0x1234")]
    [InlineData("luid_0x100000000_0x0_phys_0")]        // hex wider than 32 bits
    public void ParseGpuMemoryInstanceLuid_RejectsMalformedOrNonMatching(string instance)
    {
        var target = instance == "luid_0x00000000_0x0001368a_phys_0"
            ? new Luid(0x00013245u, 0)
            : new Luid(0x00123456u, 0);

        Assert.False(WindowsUsageTelemetryReader.IsIntelGpuMemoryCounterInstance(instance, target));
    }

    // ---- Intel GPU memory: combine ---------------------------------------

    [Fact]
    public void CombineGpuMemoryBytes_SumsDedicatedAndShared()
    {
        Assert.Equal(5ul, WindowsUsageTelemetryReader.CombineGpuMemoryBytes(2, 3));
    }

    [Fact]
    public void CombineGpuMemoryBytes_ZeroPlusZeroIsValidZero()
    {
        Assert.Equal(0ul, WindowsUsageTelemetryReader.CombineGpuMemoryBytes(0, 0));
    }

    [Theory]
    [InlineData(4ul, null)]
    [InlineData(null, 5ul)]
    [InlineData(null, null)]
    public void CombineGpuMemoryBytes_EitherCategoryUnavailableIsUnavailable(ulong? dedicated, ulong? shared)
    {
        Assert.Null(WindowsUsageTelemetryReader.CombineGpuMemoryBytes(dedicated, shared));
    }

    [Fact]
    public void CombineGpuMemoryBytes_OverflowIsUnavailable()
    {
        Assert.Null(WindowsUsageTelemetryReader.CombineGpuMemoryBytes(ulong.MaxValue, 1));
    }

    // ---- Intel GPU memory: bounded rebinding ----------------------------

    [Theory]
    [InlineData(true, true, 0, true)]
    [InlineData(false, true, 2, true)]
    [InlineData(true, false, 3, false)]
    [InlineData(false, false, 0, false)]
    public void ShouldRetryIntelGpuMemoryCounters_BoundedAndOnlyWhileIncomplete(
        bool dedicatedEmpty, bool sharedEmpty, int attempts, bool expected)
    {
        Assert.Equal(expected, WindowsUsageTelemetryReader.ShouldRetryIntelGpuMemoryCounters(dedicatedEmpty, sharedEmpty, attempts));
    }
}
