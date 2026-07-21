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
        var gpuTemp = SensorFixture.CreateDetectedSensor("gpu-temp", "Temperature", "GPU Core", 44f);
        var gpuPower = SensorFixture.CreateDetectedSensor("gpu-power", "Power", "GPU Package", 115f, unit: "W");
        var gpuLoad = SensorFixture.CreateDetectedSensor("gpu-load", "Load", "GPU Core", 62f, unit: "%");
        var cpuTemp = SensorFixture.CreateDetectedSensor("cpu-temp", "Temperature", "CPU Core", 71f, hardwareType: SensorFixture.CpuHardwareType, category: OptiSensorCategory.Cpu);
        var cpuLoad = SensorFixture.CreateDetectedSensor("cpu-load", "Load", "CPU Core", 38f, hardwareType: SensorFixture.CpuHardwareType, category: OptiSensorCategory.Cpu, unit: "%");
        var snapshot = SensorFixture.CreateSnapshot(gpuTemp, gpuPower, gpuLoad, cpuTemp, cpuLoad);

        var gpuGroup = SensorFixture.CreateGroup("g-gpu", "GPU", 0, true,
            SensorFixture.CreateSelectedSensor(gpuTemp, "", "{0:0}°C", 0),
            SensorFixture.CreateSelectedSensor(gpuPower, "", "{0:0}W", 1),
            SensorFixture.CreateSelectedSensor(gpuLoad, "", "{0:0}%", 2));
        var cpuGroup = SensorFixture.CreateGroup("g-cpu", "CPU", 1, true,
            SensorFixture.CreateSelectedSensor(cpuTemp, "", "{0:0}°C", 0),
            SensorFixture.CreateSelectedSensor(cpuLoad, "", "{0:0}%", 1));

        var line = _builder.BuildLine(snapshot, new[] { gpuGroup, cpuGroup });

        Assert.Equal("GPU 44°C 115W 62% | CPU 71°C 38%", line);
    }

    [Theory]
    [InlineData("{0:0}C")]
    [InlineData("{0:0}°C")]
    public void BuildLine_TemperatureFormats_NormalizeToSingleDegreeSymbol(string format)
    {
        var gpuTemp = SensorFixture.CreateDetectedSensor("gpu-temp", "Temperature", "GPU Core", 44f);
        var snapshot = SensorFixture.CreateSnapshot(gpuTemp);
        var group = SensorFixture.CreateGroup("g1", "", 0, true,
            SensorFixture.CreateSelectedSensor(gpuTemp, "", format, 0));

        var line = _builder.BuildLine(snapshot, new[] { group });

        Assert.Equal("44°C", line);
        Assert.DoesNotContain("°C°C", line);
        Assert.DoesNotContain("44C", line);
    }

    [Fact]
    public void BuildLine_InvalidFormat_FallsBackToIntegerWithUnitInsteadOfThrowing()
    {
        var gpuTemp = SensorFixture.CreateDetectedSensor("gpu-temp", "Temperature", "GPU Core", 44f);
        var snapshot = SensorFixture.CreateSnapshot(gpuTemp);
        var group = SensorFixture.CreateGroup("g1", "", 0, true,
            SensorFixture.CreateSelectedSensor(gpuTemp, "", "{0:0", 0));

        var line = _builder.BuildLine(snapshot, new[] { group });

        Assert.Equal("44°C", line);
    }

    [Fact]
    public void BuildLine_InvalidFormatNonTemperature_FallsBackToIntegerWithSensorUnit()
    {
        var gpuPower = SensorFixture.CreateDetectedSensor("gpu-power", "Power", "GPU Package", 120f, unit: "W");
        var snapshot = SensorFixture.CreateSnapshot(gpuPower);
        var group = SensorFixture.CreateGroup("g1", "", 0, true,
            SensorFixture.CreateSelectedSensor(gpuPower, "", "{0:0", 0));

        var line = _builder.BuildLine(snapshot, new[] { group });

        Assert.Equal("120W", line);
    }

    [Fact]
    public void BuildLine_NullSensorValue_IsSkipped()
    {
        var gpuTemp = SensorFixture.CreateDetectedSensor("gpu-temp", "Temperature", "GPU Core", null);
        var gpuPower = SensorFixture.CreateDetectedSensor("gpu-power", "Power", "GPU Package", 120f, unit: "W");
        var snapshot = SensorFixture.CreateSnapshot(gpuTemp, gpuPower);
        var group = SensorFixture.CreateGroup("g1", "", 0, true,
            SensorFixture.CreateSelectedSensor(gpuTemp, "", "{0:0}°C", 0),
            SensorFixture.CreateSelectedSensor(gpuPower, "", "{0:0}W", 1));

        var line = _builder.BuildLine(snapshot, new[] { group });

        Assert.Equal("120W", line);
    }

    [Fact]
    public void BuildLine_SensorMissingFromSnapshot_IsSkipped()
    {
        var gpuTemp = SensorFixture.CreateDetectedSensor("gpu-temp", "Temperature", "GPU Core", 44f);
        var gpuPower = SensorFixture.CreateDetectedSensor("gpu-power", "Power", "GPU Package", 120f, unit: "W");
        var snapshot = SensorFixture.CreateSnapshot(gpuPower);
        var group = SensorFixture.CreateGroup("g1", "", 0, true,
            SensorFixture.CreateSelectedSensor(gpuTemp, "", "{0:0}°C", 0),
            SensorFixture.CreateSelectedSensor(gpuPower, "", "{0:0}W", 1));

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
