using OptiSensor.Models;
using OptiSensor.Overlay;
using OptiSensor.Tests.TestData;
using Xunit;

namespace OptiSensor.Tests.Overlay;

public class OverlayOutputComposerTests
{
    private readonly OverlayOutputComposer _composer = new(new OverlayLineBuilder());

    [Fact]
    public void Compose_NoSelectedSensors_UsesDefaultGpuSelection()
    {
        var snapshot = SensorFixture.CreateSnapshot(
            SensorFixture.CreateDetectedSensor("gpu-temp", "Temperature", "GPU Core", 65f),
            SensorFixture.CreateDetectedSensor("gpu-power", "Power", "GPU Package", 120f),
            SensorFixture.CreateDetectedSensor("gpu-load", "Load", "GPU Core", 80f));
        var groups = new[] { SensorFixture.CreateGroup("ungrouped", "Ungrouped", 0, false) };

        var result = _composer.Compose(snapshot, groups);

        Assert.True(result.UsedDefaultSelection);
        Assert.Equal(0, result.TotalSelectedSensorCount);
        Assert.Equal(0, result.EnabledSelectedSensorCount);
        Assert.NotNull(result.Line);
        Assert.Contains("65°C", result.Line);
        Assert.Contains("120W", result.Line);
        Assert.Contains("80%", result.Line);
    }

    [Fact]
    public void Compose_ConfiguredSensorsAllDisabled_DoesNotFallBackToDefault()
    {
        var gpuTemp = SensorFixture.CreateDetectedSensor("gpu-temp", "Temperature", "GPU Core", 65f);
        var snapshot = SensorFixture.CreateSnapshot(gpuTemp);
        var disabledSensorInEnabledGroup = SensorFixture.CreateGroup(
            "g1", "GPU", 0, enabled: true,
            SensorFixture.CreateSelectedSensor(gpuTemp, "GPU", "{0:0}C", 0, enabled: false));

        var result = _composer.Compose(snapshot, [disabledSensorInEnabledGroup]);

        Assert.False(result.UsedDefaultSelection);
        Assert.Null(result.Line);
        Assert.Equal(1, result.TotalSelectedSensorCount);
        Assert.Equal(0, result.EnabledSelectedSensorCount);
    }

    [Fact]
    public void Compose_ConfiguredGroupDisabled_DoesNotFallBackToDefault()
    {
        var gpuTemp = SensorFixture.CreateDetectedSensor("gpu-temp", "Temperature", "GPU Core", 65f);
        var snapshot = SensorFixture.CreateSnapshot(gpuTemp);
        var enabledSensorInDisabledGroup = SensorFixture.CreateGroup(
            "g1", "GPU", 0, enabled: false,
            SensorFixture.CreateSelectedSensor(gpuTemp, "GPU", "{0:0}C", 0, enabled: true));

        var result = _composer.Compose(snapshot, [enabledSensorInDisabledGroup]);

        Assert.False(result.UsedDefaultSelection);
        Assert.Null(result.Line);
        Assert.Equal(1, result.TotalSelectedSensorCount);
        Assert.Equal(0, result.EnabledSelectedSensorCount);
    }

    [Fact]
    public void Compose_OnlyUngroupedHasSensor_PreventsDefaultFallback()
    {
        var gpuTemp = SensorFixture.CreateDetectedSensor("gpu-temp", "Temperature", "GPU Core", 65f);
        var snapshot = SensorFixture.CreateSnapshot(
            gpuTemp,
            SensorFixture.CreateDetectedSensor("gpu-power", "Power", "GPU Package", 120f),
            SensorFixture.CreateDetectedSensor("gpu-load", "Load", "GPU Core", 80f));
        var ungroupedWithSensor = SensorFixture.CreateGroup(
            "ungrouped", "Ungrouped", 0, enabled: false,
            SensorFixture.CreateSelectedSensor(gpuTemp, "GPU", "{0:0}C", 0));

        var result = _composer.Compose(snapshot, [ungroupedWithSensor]);

        Assert.Equal(1, result.TotalSelectedSensorCount);
        Assert.False(result.UsedDefaultSelection);
        // The default 3-line GPU output would never appear as a single-value line.
        Assert.NotEqual("GPU 65°C | 120W | 80%", result.Line);
    }

    [Fact]
    public void Compose_MixedEnabledState_CountsMatchGroupAndSensorEnabledFlags()
    {
        var sensorA = SensorFixture.CreateDetectedSensor("a", "Temperature", "A", 1f);
        var sensorB = SensorFixture.CreateDetectedSensor("b", "Temperature", "B", 2f);
        var sensorC = SensorFixture.CreateDetectedSensor("c", "Temperature", "C", 3f);
        var snapshot = SensorFixture.CreateSnapshot(sensorA, sensorB, sensorC);

        var enabledGroupEnabledSensor = SensorFixture.CreateGroup("g1", "G1", 0, enabled: true,
            SensorFixture.CreateSelectedSensor(sensorA, "A", "{0:0}", 0, enabled: true));
        var enabledGroupDisabledSensor = SensorFixture.CreateGroup("g2", "G2", 1, enabled: true,
            SensorFixture.CreateSelectedSensor(sensorB, "B", "{0:0}", 0, enabled: false));
        var disabledGroupEnabledSensor = SensorFixture.CreateGroup("g3", "G3", 2, enabled: false,
            SensorFixture.CreateSelectedSensor(sensorC, "C", "{0:0}", 0, enabled: true));

        var result = _composer.Compose(snapshot, [enabledGroupEnabledSensor, enabledGroupDisabledSensor, disabledGroupEnabledSensor]);

        Assert.Equal(3, result.TotalSelectedSensorCount);
        Assert.Equal(1, result.EnabledSelectedSensorCount);
        Assert.False(result.UsedDefaultSelection);
    }

    [Fact]
    public void Compose_GroupsSuppliedOutOfOrder_OutputsInOrderFieldSequence()
    {
        var cpuTemp = SensorFixture.CreateDetectedSensor("cpu", "Temperature", "CPU", 55f, hardwareType: SensorFixture.CpuHardwareType, category: OptiSensorCategory.Cpu);
        var gpuTemp = SensorFixture.CreateDetectedSensor("gpu-temp", "Temperature", "GPU", 65f);
        var gpuPower = SensorFixture.CreateDetectedSensor("gpu-power", "Power", "GPU", 120f, unit: "W");
        var snapshot = SensorFixture.CreateSnapshot(cpuTemp, gpuTemp, gpuPower);

        var cpuGroup = SensorFixture.CreateGroup("g-cpu", "CPU", 1, true,
            SensorFixture.CreateSelectedSensor(cpuTemp, "", "{0:0}°C", 0));
        var gpuGroup = SensorFixture.CreateGroup("g-gpu", "GPU", 0, true,
            SensorFixture.CreateSelectedSensor(gpuPower, "", "{0:0}W", 1),
            SensorFixture.CreateSelectedSensor(gpuTemp, "", "{0:0}°C", 0));

        // Supplied out of Order: cpuGroup (Order=1) before gpuGroup (Order=0);
        // sensors inside gpuGroup are also supplied power-then-temperature though temperature has the lower Order.
        var result = _composer.Compose(snapshot, [cpuGroup, gpuGroup]);

        Assert.Equal("GPU 65°C 120W | CPU 55°C", result.Line);
    }

    [Fact]
    public void Compose_SensorMissingFromSnapshot_IsSkippedButOthersRender()
    {
        var gpuTemp = SensorFixture.CreateDetectedSensor("gpu-temp", "Temperature", "GPU Core", 65f);
        var gpuPower = SensorFixture.CreateDetectedSensor("gpu-power", "Power", "GPU Package", 120f, unit: "W");
        var snapshot = SensorFixture.CreateSnapshot(gpuPower);
        var group = SensorFixture.CreateGroup("g1", "GPU", 0, true,
            SensorFixture.CreateSelectedSensor(gpuTemp, "", "{0:0}°C", 0),
            SensorFixture.CreateSelectedSensor(gpuPower, "", "{0:0}W", 1));

        var result = _composer.Compose(snapshot, [group]);

        Assert.False(result.UsedDefaultSelection);
        Assert.Equal(2, result.TotalSelectedSensorCount);
        Assert.Equal("GPU 120W", result.Line);
    }
}
