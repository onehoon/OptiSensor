using OptiSensor.Models;
using OptiSensor.Settings;
using OptiSensor.Tests.TestData;
using Xunit;

namespace OptiSensor.Tests.Settings;

public class AppSettingsTests
{
    [Fact]
    public void CreateCopy_MutatingCopyDoesNotAffectOriginal()
    {
        var original = new AppSettings
        {
            StartWithWindows = true,
            PublishIntervalMs = 500
        };
        original.HwInfoProfile.OverlayGroups =
        [
            SensorFixture.CreateGroup("g1", "GPU", 0, true, SensorFixture.CreateSelectedSensor("gpu-temp", "GPU", "{0:0}°C", 0))
        ];
        original.HwInfoProfile.SensorCategoryFilters[OptiSensorCategory.Gpu] = true;

        var copy = original.CreateCopy();

        // Mutate the copy's nested collections.
        copy.HwInfoProfile.OverlayGroups[0].Name = "Mutated";
        copy.HwInfoProfile.OverlayGroups[0].Sensors[0].DisplayName = "Mutated Sensor";
        copy.HwInfoProfile.OverlayGroups.Add(SensorFixture.CreateGroup("new", "New", 1, true));
        copy.HwInfoProfile.SensorCategoryFilters[OptiSensorCategory.Gpu] = false;
        copy.PublishIntervalMs = 2000;

        Assert.Equal("GPU", original.HwInfoProfile.OverlayGroups[0].Name);
        Assert.Equal("GPU", original.HwInfoProfile.OverlayGroups[0].Sensors[0].DisplayName);
        Assert.Single(original.HwInfoProfile.OverlayGroups);
        Assert.True(original.HwInfoProfile.SensorCategoryFilters[OptiSensorCategory.Gpu]);
        Assert.Equal(500, original.PublishIntervalMs);
    }

    [Fact]
    public void CreateCopy_GeneralSettingsAreCopiedByValue()
    {
        var original = new AppSettings
        {
            StartWithWindows = false,
            StartMinimized = false,
            PublishIntervalMs = 1500
        };

        var copy = original.CreateCopy();

        Assert.False(copy.StartWithWindows);
        Assert.False(copy.StartMinimized);
        Assert.Equal(1500, copy.PublishIntervalMs);
    }

    [Fact]
    public void ApplyFrom_AppliesEverythingFromSource()
    {
        var target = new AppSettings { StartWithWindows = false, PublishIntervalMs = 500 };
        var source = new AppSettings { StartWithWindows = true, PublishIntervalMs = 1000 };
        source.HwInfoProfile.OverlayGroups =
        [
            SensorFixture.CreateGroup("g1", "GPU", 0, true, SensorFixture.CreateSelectedSensor("gpu-temp", "GPU", "{0:0}°C", 0))
        ];

        target.ApplyFrom(source);

        Assert.True(target.StartWithWindows);
        Assert.Equal(1000, target.PublishIntervalMs);
        Assert.Equal("GPU", target.HwInfoProfile.OverlayGroups[0].Name);

        // Deep copy: mutating source afterward must not affect target.
        source.HwInfoProfile.OverlayGroups[0].Name = "Mutated";
        Assert.Equal("GPU", target.HwInfoProfile.OverlayGroups[0].Name);
    }

    [Fact]
    public void ApplyFrom_CopiesCompatibilityFieldsAndFiltersDeeply()
    {
        var target = new AppSettings { StartMinimized = true };
        var source = new AppSettings { StartMinimized = false };
        source.SelectedSensors =
        [
            SensorFixture.CreateSelectedSensor("legacy-sensor", "Legacy", "{0:0}", 0)
        ];
        source.OverlayGroups =
        [
            SensorFixture.CreateGroup("legacy-group", "LegacyGroup", 0, true)
        ];
        source.SensorCategoryFilters[OptiSensorCategory.Cpu] = false;
        source.SensorCategoryFilters[OptiSensorCategory.Fan] = true;

        target.ApplyFrom(source);

        Assert.False(target.StartMinimized);
        Assert.Equal("legacy-sensor", target.SelectedSensors[0].SensorId);
        Assert.Equal("LegacyGroup", target.OverlayGroups[0].Name);
        Assert.False(target.SensorCategoryFilters[OptiSensorCategory.Cpu]);
        Assert.True(target.SensorCategoryFilters[OptiSensorCategory.Fan]);

        // Deep copy: mutating source's compatibility fields afterward must not affect target.
        source.SelectedSensors[0].DisplayName = "Mutated";
        source.OverlayGroups[0].Name = "Mutated";
        source.SensorCategoryFilters[OptiSensorCategory.Cpu] = true;

        Assert.Equal("legacy-sensor", target.SelectedSensors[0].SensorId);
        Assert.NotEqual("Mutated", target.SelectedSensors[0].DisplayName);
        Assert.Equal("LegacyGroup", target.OverlayGroups[0].Name);
        Assert.False(target.SensorCategoryFilters[OptiSensorCategory.Cpu]);
    }

    [Fact]
    public void ReplaceOverlayGroups_UpdatesActiveProfileSelectedSensors()
    {
        var settings = new AppSettings();
        var sensorA = SensorFixture.CreateSelectedSensor("a", "A", "{0:0}", order: 5);
        var sensorB = SensorFixture.CreateSelectedSensor("b", "B", "{0:0}", order: 1);
        var group = SensorFixture.CreateGroup("g1", "Group", 0, true, sensorA, sensorB);

        settings.ReplaceOverlayGroups([group]);

        var flattened = settings.HwInfoProfile.SelectedSensors;
        Assert.Equal(2, flattened.Count);
        Assert.Equal("b", flattened[0].SensorId);
        Assert.Equal(0, flattened[0].Order);
        Assert.Equal("a", flattened[1].SensorId);
        Assert.Equal(1, flattened[1].Order);
    }

    [Fact]
    public void ReplaceOverlayGroups_DoesNotTouchLegacyTopLevelFields()
    {
        var settings = new AppSettings();
        var sensor = SensorFixture.CreateSelectedSensor("hwinfo-sensor", "HwInfo", "{0:0}", 0);
        var group = SensorFixture.CreateGroup("hwinfo-group", "HwInfoGroup", 0, true, sensor);

        settings.ReplaceOverlayGroups([group]);

        Assert.Empty(settings.OverlayGroups);
        Assert.Empty(settings.SelectedSensors);
    }

    [Fact]
    public void ReplaceOverlayGroups_NormalizesOrderFromZeroSequentially()
    {
        var settings = new AppSettings();
        var sensorA = SensorFixture.CreateSelectedSensor("a", "A", "{0:0}", order: 7);
        var sensorB = SensorFixture.CreateSelectedSensor("b", "B", "{0:0}", order: 3);
        var groupHigh = SensorFixture.CreateGroup("g-high", "High", order: 9, enabled: true, sensorA, sensorB);
        var groupLow = SensorFixture.CreateGroup("g-low", "Low", order: 2, enabled: true);

        settings.ReplaceOverlayGroups([groupHigh, groupLow]);

        var snapshot = settings.GetOverlayGroupsSnapshot();
        Assert.Equal("Low", snapshot[0].Name);
        Assert.Equal(0, snapshot[0].Order);
        Assert.Equal("High", snapshot[1].Name);
        Assert.Equal(1, snapshot[1].Order);
        Assert.Equal("B", snapshot[1].Sensors[0].DisplayName);
        Assert.Equal(0, snapshot[1].Sensors[0].Order);
        Assert.Equal("A", snapshot[1].Sensors[1].DisplayName);
        Assert.Equal(1, snapshot[1].Sensors[1].Order);
    }

    [Fact]
    public void ReplaceOverlayGroups_MutatingInputAfterCallDoesNotAffectStoredSnapshot()
    {
        var settings = new AppSettings();
        var group = SensorFixture.CreateGroup("g1", "Original", 0, true);

        settings.ReplaceOverlayGroups([group]);
        group.Name = "Mutated After Call";

        var snapshot = settings.GetOverlayGroupsSnapshot();
        Assert.Equal("Original", snapshot[0].Name);
    }

    [Fact]
    public void Deserialize_ToleratesObsoleteLibreProfileProperty()
    {
        const string json = """
        {
          "sensorSource": "Libre",
          "libreProfile": { "overlayGroups": [], "selectedSensors": [], "sensorCategoryFilters": {} },
          "hwInfoProfile": { "overlayGroups": [], "selectedSensors": [], "sensorCategoryFilters": {} }
        }
        """;

        var settings = AppSettings.Deserialize(json);

        Assert.NotNull(settings);
        Assert.NotNull(settings.HwInfoProfile);
    }
}
