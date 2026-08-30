using OptiSensor.Claw;
using Xunit;

namespace OptiSensor.Tests.Claw;

public class IgclGpuTelemetryTests
{
    // ---- CalculateGpuUsage -------------------------------------------------

    [Fact]
    public void CalculateGpuUsage_NormalDelta()
    {
        Assert.Equal(50.0, IgclGpuUsageTracker.CalculateGpuUsage(100, 20, 110, 25));
    }

    [Fact]
    public void CalculateGpuUsage_HardwareVectorDelta()
    {
        Assert.Equal(95.0, IgclGpuUsageTracker.CalculateGpuUsage(10.0, 5.0, 11.0, 5.95)!.Value, 3);
    }

    [Fact]
    public void CalculateGpuUsage_ClampsAbove100()
    {
        Assert.Equal(100.0, IgclGpuUsageTracker.CalculateGpuUsage(10.0, 5.0, 11.0, 7.0));
    }

    [Fact]
    public void CalculateGpuUsage_ZeroActivityDeltaIsValidZero()
    {
        Assert.Equal(0.0, IgclGpuUsageTracker.CalculateGpuUsage(10.0, 5.0, 11.0, 5.0));
    }

    [Theory]
    [InlineData(10.0, 5.0, 10.0, 6.0)] // timestamp not increasing
    [InlineData(10.0, 5.0, 9.0, 6.0)]  // timestamp decreasing
    [InlineData(10.0, 5.0, 11.0, 4.0)] // activity rollback
    public void CalculateGpuUsage_InvalidProgressionIsUnavailable(double pt, double pa, double ct, double ca)
    {
        Assert.Null(IgclGpuUsageTracker.CalculateGpuUsage(pt, pa, ct, ca));
    }

    [Theory]
    [InlineData(double.NaN, 5.0, 11.0, 6.0)]
    [InlineData(10.0, double.NaN, 11.0, 6.0)]
    [InlineData(10.0, 5.0, double.PositiveInfinity, 6.0)]
    [InlineData(10.0, 5.0, 11.0, double.NegativeInfinity)]
    public void CalculateGpuUsage_NonFiniteInputIsUnavailable(double pt, double pa, double ct, double ca)
    {
        Assert.Null(IgclGpuUsageTracker.CalculateGpuUsage(pt, pa, ct, ca));
    }

    // ---- IgclGpuUsageTracker state contract ------------------------------

    [Fact]
    public void Update_FirstValidSampleHasNoUsageThenSecondDoes()
    {
        var tracker = new IgclGpuUsageTracker();

        Assert.Null(tracker.Update(100, 20));
        Assert.Equal(50.0, tracker.Update(110, 25));
    }

    [Fact]
    public void Update_ResetClearsBaseline()
    {
        var tracker = new IgclGpuUsageTracker();

        tracker.Update(100, 20);
        tracker.Reset();

        Assert.Null(tracker.Update(110, 25));
    }

    [Fact]
    public void Update_InvalidSampleResetsBaseline()
    {
        var tracker = new IgclGpuUsageTracker();

        tracker.Update(100, 20);
        Assert.Null(tracker.Update(null, 25));   // missing timestamp -> baseline reset
        Assert.Null(tracker.Update(110, 25));    // new baseline
        Assert.Equal(50.0, tracker.Update(120, 30));
    }

    [Theory]
    [InlineData(-1.0, 20.0)]
    [InlineData(100.0, -1.0)]
    public void Update_NegativeInputIsUnavailable(double timestamp, double activity)
    {
        Assert.Null(new IgclGpuUsageTracker().Update(timestamp, activity));
    }

    // ---- DecodeItem -----------------------------------------------------

    [Fact]
    public void DecodeItem_UnsupportedIsUnavailable()
    {
        Assert.Null(IgclGpuTelemetryReader.DecodeItem(new IgclGpuTelemetryReader.IgclItem { Supported = 0, Type = 9, D = 42.0 }));
    }

    [Fact]
    public void DecodeItem_DecodesFiniteDouble()
    {
        Assert.Equal(42.5, IgclGpuTelemetryReader.DecodeItem(new IgclGpuTelemetryReader.IgclItem { Supported = 1, Type = 9, D = 42.5 }));
    }

    [Fact]
    public void DecodeItem_DecodesSignedIntegerAndZero()
    {
        Assert.Equal(-100.0, IgclGpuTelemetryReader.DecodeItem(new IgclGpuTelemetryReader.IgclItem { Supported = 1, Type = 4, I32 = -100 }));
        Assert.Equal(0.0, IgclGpuTelemetryReader.DecodeItem(new IgclGpuTelemetryReader.IgclItem { Supported = 1, Type = 8, F = 0f }));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void DecodeItem_NonFiniteIsUnavailable(double value)
    {
        Assert.Null(IgclGpuTelemetryReader.DecodeItem(new IgclGpuTelemetryReader.IgclItem { Supported = 1, Type = 9, D = value }));
    }

    [Fact]
    public void DecodeItem_UnknownTypeIsUnavailable()
    {
        Assert.Null(IgclGpuTelemetryReader.DecodeItem(new IgclGpuTelemetryReader.IgclItem { Supported = 1, Type = 99, U64 = 5 }));
    }

    // ---- provider reset policy ----------------------------------------

    [Theory]
    [InlineData(1, 3, false)] // transient failure
    [InlineData(2, 3, false)]
    [InlineData(3, 3, true)]  // third consecutive provider failure -> reset
    [InlineData(4, 3, true)]
    [InlineData(3, 0, false)] // disabled
    public void ShouldResetProvider_OnlyAtOrAboveThreshold(int consecutiveFailures, int threshold, bool expected)
    {
        Assert.Equal(expected, IgclGpuTelemetryReader.ShouldResetProvider(consecutiveFailures, threshold));
    }

    [Fact]
    public void Sample_OnUninitializedReaderStaysNullWithoutTouchingLifecycle()
    {
        // No ControlLib.dll / Arc device on the CI host: the reader never initializes, so Sample()
        // early-returns before the provider-failure path. Repeated calls must stay a quiet null.
        using var reader = new IgclGpuTelemetryReader();

        Assert.False(reader.Initialized);
        Assert.Null(reader.Sample());
        Assert.Null(reader.Sample());
        Assert.Null(reader.Sample());
        Assert.False(reader.Initialized);
    }
}
