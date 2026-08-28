using OptiSensor.Claw;
using Xunit;

namespace OptiSensor.Tests.Claw;

public class WindowsPowerTelemetryTests
{
    [Fact]
    public void Decode_OnBatteryWithRemainingTime()
    {
        var snapshot = WindowsPowerTelemetry.Decode(new WindowsPowerTelemetry.SYSTEM_POWER_STATUS
        {
            BatteryLifePercent = 72,
            ACLineStatus = 0,
            BatteryLifeTime = 9000,
        });

        Assert.Equal(72, snapshot.BatteryPercent);
        Assert.Equal(true, snapshot.OnBattery);
        Assert.Equal(150, snapshot.RemainingMinutes);
    }

    [Fact]
    public void Decode_AcLineStatusOneIsNotOnBattery()
    {
        var snapshot = WindowsPowerTelemetry.Decode(new WindowsPowerTelemetry.SYSTEM_POWER_STATUS
        {
            BatteryLifePercent = 72,
            ACLineStatus = 1,
            BatteryLifeTime = 9000,
        });

        Assert.Equal(false, snapshot.OnBattery);
    }

    [Fact]
    public void Decode_SentinelValuesAreAllUnavailable()
    {
        var snapshot = WindowsPowerTelemetry.Decode(new WindowsPowerTelemetry.SYSTEM_POWER_STATUS
        {
            BatteryLifePercent = 255,
            ACLineStatus = 255,
            BatteryLifeTime = uint.MaxValue,
        });

        Assert.Null(snapshot.BatteryPercent);
        Assert.Null(snapshot.OnBattery);
        Assert.Null(snapshot.RemainingMinutes);
    }
}
