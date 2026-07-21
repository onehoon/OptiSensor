using OptiSensor.Models;
using OptiSensor.Overlay;
using OptiSensor.Tests.TestData;
using Xunit;

namespace OptiSensor.Tests.Overlay;

public class OverlayLineBuilderTests
{
    private readonly OverlayLineBuilder _builder = new();

    [Fact]
    public void BuildLine_MultipleGroups_JoinsSensorsWithSpaceAndGroupsWithPipe()
    {
        var snapshot = SensorFixture.CreateSnapshot(
            SensorFixture.CreateDetectedSensor("gpu-temp", "Temperature", "GPU Core", 44f),
            SensorFixture.CreateDetectedSensor("gpu-power", "Power", "GPU Package", 115f),
            SensorFixture.CreateDetectedSensor("gpu-load", "Load", "GPU Core", 62f),
            SensorFixture.CreateDetectedSensor("cpu-temp", "Temperature", "CPU Core", 71f, hardwareType: SensorFixture.CpuHardwareType, category: OptiSensorCategory.Cpu),
            SensorFixture.CreateDetectedSensor("cpu-load", "Load", "CPU Core", 38f, hardwareType: SensorFixture.CpuHardwareType, category: OptiSensorCategory.Cpu));

        var gpuGroup = SensorFixture.CreateGroup("g-gpu", "GPU", 0, true,
            SensorFixture.CreateSelectedSensor("gpu-temp", "", "{0:0}°C", 0),
            SensorFixture.CreateSelectedSensor("gpu-power", "", "{0:0}W", 1),
            SensorFixture.CreateSelectedSensor("gpu-load", "", "{0:0}%", 2));
        var cpuGroup = SensorFixture.CreateGroup("g-cpu", "CPU", 1, true,
            SensorFixture.CreateSelectedSensor("cpu-temp", "", "{0:0}°C", 0),
            SensorFixture.CreateSelectedSensor("cpu-load", "", "{0:0}%", 1));

        var line = _builder.BuildLine(snapshot, new[] { gpuGroup, cpuGroup });

        Assert.Equal("GPU 44°C 115W 62% | CPU 71°C 38%", line);
    }

    [Theory]
    [InlineData("{0:0}C")]
    [InlineData("{0:0}°C")]
    public void BuildLine_TemperatureFormats_NormalizeToSingleDegreeSymbol(string format)
    {
        var snapshot = SensorFixture.CreateSnapshot(SensorFixture.CreateDetectedSensor("gpu-temp", "Temperature", "GPU Core", 44f));
        var group = SensorFixture.CreateGroup("g1", "", 0, true,
            SensorFixture.CreateSelectedSensor("gpu-temp", "", format, 0));

        var line = _builder.BuildLine(snapshot, new[] { group });

        Assert.Equal("44°C", line);
        Assert.DoesNotContain("°C°C", line);
        Assert.DoesNotContain("44C", line);
    }

    [Fact]
    public void BuildLine_InvalidFormat_FallsBackToIntegerWithUnitInsteadOfThrowing()
    {
        var snapshot = SensorFixture.CreateSnapshot(SensorFixture.CreateDetectedSensor("gpu-temp", "Temperature", "GPU Core", 44f));
        var group = SensorFixture.CreateGroup("g1", "", 0, true,
            SensorFixture.CreateSelectedSensor("gpu-temp", "", "{0:0", 0));

        var line = _builder.BuildLine(snapshot, new[] { group });

        Assert.Equal("44°C", line);
    }

    [Fact]
    public void BuildLine_InvalidFormatNonTemperature_FallsBackToIntegerWithSensorUnit()
    {
        var snapshot = SensorFixture.CreateSnapshot(SensorFixture.CreateDetectedSensor("gpu-power", "Power", "GPU Package", 120f, unit: "W"));
        var group = SensorFixture.CreateGroup("g1", "", 0, true,
            SensorFixture.CreateSelectedSensor("gpu-power", "", "{0:0", 0, sensorType: "Power", unit: "W"));

        var line = _builder.BuildLine(snapshot, new[] { group });

        Assert.Equal("120W", line);
    }

    [Fact]
    public void BuildLine_NullSensorValue_IsSkipped()
    {
        var snapshot = SensorFixture.CreateSnapshot(
            SensorFixture.CreateDetectedSensor("gpu-temp", "Temperature", "GPU Core", null),
            SensorFixture.CreateDetectedSensor("gpu-power", "Power", "GPU Package", 120f));
        var group = SensorFixture.CreateGroup("g1", "", 0, true,
            SensorFixture.CreateSelectedSensor("gpu-temp", "", "{0:0}°C", 0),
            SensorFixture.CreateSelectedSensor("gpu-power", "", "{0:0}W", 1));

        var line = _builder.BuildLine(snapshot, new[] { group });

        Assert.Equal("120W", line);
    }

    [Fact]
    public void BuildLine_SensorMissingFromSnapshot_IsSkipped()
    {
        var snapshot = SensorFixture.CreateSnapshot(SensorFixture.CreateDetectedSensor("gpu-power", "Power", "GPU Package", 120f));
        var group = SensorFixture.CreateGroup("g1", "", 0, true,
            SensorFixture.CreateSelectedSensor("gpu-temp", "", "{0:0}°C", 0),
            SensorFixture.CreateSelectedSensor("gpu-power", "", "{0:0}W", 1));

        var line = _builder.BuildLine(snapshot, new[] { group });

        Assert.Equal("120W", line);
    }

    [Fact]
    public void BuildLine_NoSensorsRenderable_ReturnsNull()
    {
        var snapshot = SensorFixture.CreateSnapshot();
        var group = SensorFixture.CreateGroup("g1", "", 0, true,
            SensorFixture.CreateSelectedSensor("missing", "", "{0:0}", 0));

        var line = _builder.BuildLine(snapshot, new[] { group });

        Assert.Null(line);
    }

    [Fact]
    public void BuildDefaultLine_PicksGpuSensorsAndIgnoresCpu()
    {
        var snapshot = SensorFixture.CreateSnapshot(
            SensorFixture.CreateDetectedSensor("cpu-temp", "Temperature", "CPU Core", 999f, hardwareType: SensorFixture.CpuHardwareType, category: OptiSensorCategory.Cpu),
            SensorFixture.CreateDetectedSensor("gpu-temp-1", "Temperature", "GPU Core", 44f, hardwareType: SensorFixture.GpuHardwareType),
            SensorFixture.CreateDetectedSensor("gpu-temp-2", "Temperature", "GPU Hot Spot", 55f, hardwareType: SensorFixture.GpuHardwareType),
            SensorFixture.CreateDetectedSensor("gpu-power", "Power", "GPU Package", 115f, hardwareType: SensorFixture.GpuHardwareType),
            SensorFixture.CreateDetectedSensor("gpu-load", "Load", "GPU Core", 62f, hardwareType: SensorFixture.GpuHardwareType));

        var line = _builder.BuildDefaultLine(snapshot);

        // BuildDefaultLine feeds a flat sensor list through the "|"-joined overload,
        // unlike the group overload which joins sensors within a group with spaces.
        Assert.Equal("GPU 44°C | 115W | 62%", line);
        Assert.DoesNotContain("999", line);
    }
}
