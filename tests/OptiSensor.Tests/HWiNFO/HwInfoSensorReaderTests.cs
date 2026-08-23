using Hwinfo.SharedMemory;
using OptiSensor.HWiNFO;
using OptiSensor.Models;
using Xunit;

namespace OptiSensor.Tests.HWiNFO;

public sealed class HwInfoSensorReaderTests
{
    [Fact]
    public void Map_PreservesStableIdAndUserLabels()
    {
        var reading = new SensorReading(
            ReadingId: 456,
            ReadingType: SensorType.Temp,
            LabelOrig: "GPU Temperature",
            LabelUser: "Core Temp",
            Unit: "°C",
            Value: 55,
            ValueMin: 0,
            ValueMax: 100,
            ValueAvg: 55,
            Sensor: new Sensor(123, 2, "GPU [#0]", "My GPU"));

        var mapped = HwInfoSensorReader.Map(reading);

        Assert.Equal("hwinfo/123/2/456", mapped.SensorId);
        Assert.Equal("My GPU", mapped.HardwareName);
        Assert.Equal("Core Temp", mapped.SensorName);
        Assert.Equal("°C", mapped.Unit);
        Assert.Equal(55, mapped.Value);
        Assert.Equal(OptiSensorCategory.Gpu, mapped.Category);
    }

    [Fact]
    public void Map_FallsBackToOriginalNames()
    {
        var reading = new SensorReading(
            1,
            SensorType.Temp,
            "CPU Temperature",
            " ",
            "°C",
            42,
            0,
            100,
            42,
            new Sensor(7, 0, "CPU [#0]", ""));

        var mapped = HwInfoSensorReader.Map(reading);

        Assert.Equal("CPU [#0]", mapped.HardwareName);
        Assert.Equal("CPU Temperature", mapped.SensorName);
        Assert.Equal(OptiSensorCategory.Cpu, mapped.Category);
    }

    [Theory]
    [InlineData(SensorType.Fan, 3)]
    [InlineData(SensorType.Power, 2)]
    [InlineData(SensorType.Volt, 2)]
    [InlineData(SensorType.Current, 2)]
    public void Map_UsesTypedSensorTypeClassification(SensorType type, int expectedCategory)
    {
        var reading = new SensorReading(
            1,
            type,
            "Reading",
            "",
            "",
            1,
            0,
            1,
            1,
            new Sensor(7, 0, "Other", ""));

        Assert.Equal((OptiSensorCategory)expectedCategory, HwInfoSensorReader.Map(reading).Category);
    }
}
