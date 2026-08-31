using OptiSensor.Claw;
using Xunit;

namespace OptiSensor.Tests.Claw;

public class BatteryPowerEstimatorTests
{
    [Fact]
    public void Estimate_WaitsForTenDcSamplesThenUsesEcPowerAndCapacity()
    {
        var estimator = new BatteryPowerEstimator();

        for (var seconds = 1; seconds <= 9; seconds++)
            estimator.Observe(true, 40, TimeSpan.FromSeconds(seconds));

        Assert.False(estimator.Ready);
        Assert.Null(estimator.EstimateRemainingMinutes(60_000));

        estimator.Observe(true, 42, TimeSpan.FromSeconds(10));

        Assert.True(estimator.Ready);
        Assert.Equal(10, estimator.SampleCount);
        Assert.Equal(90, estimator.EstimateRemainingMinutes(60_000));
    }

    [Fact]
    public void Observe_AcTransitionClearsHistoryAndInvalidDcSampleDoesNotAddOne()
    {
        var estimator = new BatteryPowerEstimator();
        estimator.Observe(true, 40, TimeSpan.FromSeconds(1));
        estimator.Observe(true, null, TimeSpan.FromSeconds(2));

        Assert.Equal(1, estimator.SampleCount);

        estimator.Observe(false, 40, TimeSpan.FromSeconds(3));

        Assert.Equal(0, estimator.SampleCount);
        Assert.False(estimator.Ready);
    }

    [Fact]
    public void ObserveBatteryPower_InvalidDcSampleAfterReadyClearsStaleEstimate()
    {
        var estimator = new BatteryPowerEstimator();
        for (var seconds = 1; seconds <= 10; seconds++)
        {
            ClawTelemetrySampler.ObserveBatteryPower(
                estimator, onBattery: true, batteryDischargePowerW: 40,
                TimeSpan.FromSeconds(seconds));
        }

        Assert.True(estimator.Ready);
        Assert.NotNull(estimator.EstimateRemainingMinutes(60_000));

        ClawTelemetrySampler.ObserveBatteryPower(
            estimator, onBattery: true, batteryDischargePowerW: null,
            TimeSpan.FromSeconds(11));

        Assert.Equal(0, estimator.SampleCount);
        Assert.False(estimator.Ready);
        Assert.Null(estimator.EstimateRemainingMinutes(60_000));
    }
}
