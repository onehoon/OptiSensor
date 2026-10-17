using LibreHardwareMonitor.Hardware;
using OptiSensor.Libre;
using Xunit;

namespace OptiSensor.Tests.Libre;

public sealed class LibreSensorValueNormalizerTests
{
    [Theory]
    [InlineData(1073741824f, 1f)]
    [InlineData(536870912f, 0.5f)]
    public void Normalize_DataBytesToGigabytes(float bytes, float expectedGigabytes)
    {
        var normalized = LibreSensorValueNormalizer.Normalize(SensorType.Data, bytes);

        Assert.Equal(expectedGigabytes, normalized);
    }

    [Theory]
    [InlineData(1048576f, 1f)]
    [InlineData(524288f, 0.5f)]
    public void Normalize_ThroughputBytesPerSecondToMegabytesPerSecond(float bytesPerSecond, float expectedMegabytesPerSecond)
    {
        var normalized = LibreSensorValueNormalizer.Normalize(SensorType.Throughput, bytesPerSecond);

        Assert.Equal(expectedMegabytesPerSecond, normalized);
    }

    [Fact]
    public void Normalize_OtherSensorTypesPreservesValue()
    {
        var normalized = LibreSensorValueNormalizer.Normalize(SensorType.Power, 42f);

        Assert.Equal(42f, normalized);
    }

    [Fact]
    public void Normalize_NullValueRemainsNull()
    {
        var normalized = LibreSensorValueNormalizer.Normalize(SensorType.Data, null);

        Assert.Null(normalized);
    }
}
