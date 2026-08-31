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
        Assert.Equal("SensorTypeTemp", mapped.SensorType);
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
    [InlineData("°C")]   // already correct
    [InlineData("¡ÆC")] // CP949 degree sign mis-decoded as Latin-1 by Hwinfo.SharedMemory
    [InlineData("")]          // HWiNFO reported no unit
    public void Map_NormalizesTemperatureUnitToCanonicalCelsius(string rawUnit)
    {
        var reading = new SensorReading(
            1, SensorType.Temp, "GPU Temperature", "", rawUnit, 55, 0, 100, 55,
            new Sensor(1, 0, "GPU [#0]", ""));

        Assert.Equal("°C", HwInfoSensorReader.Map(reading).Unit);
    }

    [Theory]
    [InlineData(SensorType.Power, "W")]
    [InlineData(SensorType.Clock, "MHz")]
    [InlineData(SensorType.Usage, "%")]
    [InlineData(SensorType.Other, "MB")]
    public void Map_PassesNonTemperatureUnitsThroughUnchanged(SensorType type, string unit)
    {
        var reading = new SensorReading(
            1, type, "Reading", "", unit, 1, 0, 1, 1,
            new Sensor(1, 0, "GPU [#0]", ""));

        Assert.Equal(unit, HwInfoSensorReader.Map(reading).Unit);
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
