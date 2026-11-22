using OptiSensor.Native;
using Xunit;

namespace OptiSensor.Tests.Libre;

public sealed class SensorValueRetentionTests
{
    [Fact]
    public void KeepsLastValueForTwoFailedPollsAndExpiresOnThird()
    {
        var retention = new SensorValueRetention();

        Assert.Null(retention.LastValidValue);
        Assert.Null(retention.RecordPollResult(true, true, null));
        Assert.Null(retention.RecordPollResult(true, false, null));
        Assert.Equal(65f, retention.RecordPollResult(true, true, 65f));
        Assert.Equal(65f, retention.RecordPollResult(true, false, 65f));
        Assert.Equal(65f, retention.RecordPollResult(true, true, null));
        Assert.Null(retention.RecordPollResult(true, false, 65f));
        Assert.Null(retention.RecordPollResult(true, false, 65f));
        Assert.Equal(66f, retention.RecordPollResult(true, true, 66f));
    }

    [Fact]
    public void SkippedPollsDoNotCountAsMisses()
    {
        var retention = new SensorValueRetention();

        retention.RecordPollResult(true, true, 65f);
        retention.RecordPollResult(true, false, null);

        Assert.Equal(65f, retention.RecordPollResult(false, false, null));
        Assert.Equal(65f, retention.RecordPollResult(false, false, null));
        Assert.Equal(65f, retention.RecordPollResult(true, false, null));
        Assert.Equal(65f, retention.RecordPollResult(false, false, null));
        Assert.Null(retention.RecordPollResult(true, false, null));
    }

    [Fact]
    public void AcceptsZeroAndRejectsNonFiniteValues()
    {
        var retention = new SensorValueRetention();

        Assert.Equal(0f, retention.RecordPollResult(true, true, 0f));
        Assert.Equal(0f, retention.RecordPollResult(true, true, float.NaN));
        Assert.Equal(0f, retention.RecordPollResult(true, true, float.PositiveInfinity));
        Assert.Null(retention.RecordPollResult(true, true, null));
    }

    [Fact]
    public void RetentionStateIsIndependentPerSensor()
    {
        var firstSensor = new SensorValueRetention();
        var secondSensor = new SensorValueRetention();

        firstSensor.RecordPollResult(true, true, 42f);
        Assert.Null(secondSensor.RecordPollResult(true, true, null));
        Assert.Equal(42f, firstSensor.LastValidValue);
        Assert.Null(secondSensor.LastValidValue);
    }
}
