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

    // ---- Intel GPU memory: recovery policy ------------------------------

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    public void NeedsIntelGpuMemoryBinding_TrueWhileEitherCategoryIsUnbound(
        bool dedicatedEmpty, bool sharedEmpty, bool expected)
    {
        Assert.Equal(expected, WindowsUsageTelemetryReader.NeedsIntelGpuMemoryBinding(dedicatedEmpty, sharedEmpty));
    }

    [Theory]
    [InlineData(2, 3, false)] // below threshold: keep the bound counters
    [InlineData(3, 3, true)]  // threshold reached: release for rebind
    [InlineData(4, 3, true)]
    [InlineData(3, 0, false)] // threshold 0: disabled
    public void ShouldReleaseIntelGpuMemoryCounters_OnlyAtOrAboveThreshold(
        int consecutiveFailures, int failureThreshold, bool expected)
    {
        Assert.Equal(expected, WindowsUsageTelemetryReader.ShouldReleaseIntelGpuMemoryCounters(consecutiveFailures, failureThreshold));
    }

    [Theory]
    [InlineData(3, 29, false)] // attempts exhausted, cooldown not yet reached
    [InlineData(3, 30, true)]  // attempts exhausted, cooldown reached: re-arm
    [InlineData(3, 31, true)]
    [InlineData(2, 30, false)] // attempts not exhausted: no cooldown-driven re-arm
    public void ShouldRearmIntelGpuMemoryCounters_OnlyAfterCooldownWhenAttemptsExhausted(
        int attempts, int cooldownSamples, bool expected)
    {
        Assert.Equal(expected, WindowsUsageTelemetryReader.ShouldRearmIntelGpuMemoryCounters(
            attempts, cooldownSamples, maxAttempts: 3, cooldownThreshold: 30));
    }

    // ---- VRAM failure isolation ----------------------------------------

    [Fact]
    public void WindowsUsageSnapshot_CarriesCpuAndRamWithIntelVramUnavailable()
    {
        var snapshot = new WindowsUsageSnapshot(CpuUsagePercent: 42.0, SystemMemoryUsedBytes: 50, IntelGpuMemoryUsedBytes: null);

        Assert.Equal(42.0, snapshot.CpuUsagePercent);
        Assert.Equal(50ul, snapshot.SystemMemoryUsedBytes);
        Assert.Null(snapshot.IntelGpuMemoryUsedBytes);
    }

    [Fact]
    public void Sample_DoesNotReturnNullJustBecauseIntelVramIsUnavailable()
    {
        using var reader = new WindowsUsageTelemetryReader();
        Assert.True(reader.Initialize());

        // First real sample after priming.
        WindowsUsageSnapshot? snapshot = null;
        for (var i = 0; i < 4 && (snapshot is null || snapshot.SystemMemoryUsedBytes is null); i++)
            snapshot = reader.Sample();

        // RAM comes from GlobalMemoryStatusEx and is always available; the CI host has no Intel GPU
        // so VRAM stays null - the whole sample must still be returned.
        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot!.SystemMemoryUsedBytes);
    }
}
