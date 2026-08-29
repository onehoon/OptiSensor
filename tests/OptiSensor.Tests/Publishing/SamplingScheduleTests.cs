using OptiSensor.Publishing;
using Xunit;

namespace OptiSensor.Tests.Publishing;

/// <summary>
/// The sampling loop advances its monotonic due-times with <see cref="SensorPublishService.AdvanceDueTime"/>
/// using a timestamp taken *after* each (possibly slow) native read. A read that overran one or more
/// intervals must skip the elapsed deadlines rather than fire a catch-up burst.
/// </summary>
public sealed class SamplingScheduleTests
{
    [Fact]
    public void FastSample_AdvancesByExactlyOneInterval()
    {
        // Sample due at 1000, finished at 1005: next read is one full interval later.
        Assert.Equal(2000, SensorPublishService.AdvanceDueTime(previousDueMs: 1000, intervalMs: 1000, nowMs: 1005));
    }

    [Fact]
    public void SlowCoreSample_SkipsTheDeadlineThatElapsedWhileSampling()
    {
        // Core sample due at 1000 but the WMI/driver read took ~1600 ms and finished at 2600.
        // The 2000 deadline already passed - the next read waits for 3000, not an immediate resample.
        var next = SensorPublishService.AdvanceDueTime(previousDueMs: 1000, intervalMs: 1000, nowMs: 2600);

        Assert.Equal(3000, next);
        Assert.True(next > 2600, "A slow read must not leave the next due-time in the past.");
    }

    [Fact]
    public void VerySlowSample_SkipsAllElapsedDeadlinesWithoutCatchUpBurst()
    {
        // A 3.5 s stall over a 1 s cadence: 2000/3000/4000 are all missed and skipped in one step.
        Assert.Equal(5000, SensorPublishService.AdvanceDueTime(previousDueMs: 1000, intervalMs: 1000, nowMs: 4500));
    }

    [Fact]
    public void SlowBatterySample_SkipsElapsedFiveSecondDeadlines()
    {
        // Battery due at 5000, read finished at 12000: skip the 10000 deadline, next is 15000.
        Assert.Equal(15000, SensorPublishService.AdvanceDueTime(previousDueMs: 5000, intervalMs: 5000, nowMs: 12000));
    }

    [Fact]
    public void ExactDeadlineHit_StillAdvancesPastNow()
    {
        // now == the freshly computed next due-time must not leave a zero-length wait.
        Assert.Equal(3000, SensorPublishService.AdvanceDueTime(previousDueMs: 1000, intervalMs: 1000, nowMs: 2000));
    }
}
