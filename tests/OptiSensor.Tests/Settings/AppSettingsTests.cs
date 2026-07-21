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
            PublishIntervalMs = 500,
            SensorSource = SensorSourceKind.Libre
        };
        original.LibreProfile.OverlayGroups =
        [
            SensorFixture.CreateGroup("g1", "GPU", 0, true, SensorFixture.CreateSelectedSensor("gpu-temp", "GPU", "{0:0}°C", 0))
        ];
        original.LibreProfile.SensorCategoryFilters[OptiSensorCategory.Gpu] = true;
        original.HwInfoProfile.OverlayGroups =
        [
            SensorFixture.CreateGroup("g2", "CPU", 0, true, SensorFixture.CreateSelectedSensor("cpu-temp", "CPU", "{0:0}°C", 0))
        ];

        var copy = original.CreateCopy();

        // Mutate the copy's nested collections.
        copy.LibreProfile.OverlayGroups[0].Name = "Mutated";
        copy.LibreProfile.OverlayGroups[0].Sensors[0].DisplayName = "Mutated Sensor";
        copy.LibreProfile.OverlayGroups.Add(SensorFixture.CreateGroup("new", "New", 1, true));
        copy.LibreProfile.SensorCategoryFilters[OptiSensorCategory.Gpu] = false;
        copy.HwInfoProfile.OverlayGroups.Clear();
        copy.PublishIntervalMs = 2000;

        Assert.Equal("GPU", original.LibreProfile.OverlayGroups[0].Name);
        Assert.Equal("GPU", original.LibreProfile.OverlayGroups[0].Sensors[0].DisplayName);
        Assert.Single(original.LibreProfile.OverlayGroups);
        Assert.True(original.LibreProfile.SensorCategoryFilters[OptiSensorCategory.Gpu]);
        Assert.Single(original.HwInfoProfile.OverlayGroups);
        Assert.Equal(500, original.PublishIntervalMs);
    }

    [Fact]
    public void CreateCopy_GeneralSettingsAreCopiedByValue()
    {
        var original = new AppSettings
        {
            StartWithWindows = false,
            StartMinimized = false,
            PublishIntervalMs = 1500,
            SensorSource = SensorSourceKind.HwInfo
        };

        var copy = original.CreateCopy();

        Assert.False(copy.StartWithWindows);
        Assert.False(copy.StartMinimized);
        Assert.Equal(1500, copy.PublishIntervalMs);
        Assert.Equal(SensorSourceKind.HwInfo, copy.SensorSource);
    }

    [Fact]
    public void ApplyFrom_WithoutPreservingSensorSource_AppliesEverythingFromSource()
    {
        var target = new AppSettings { SensorSource = SensorSourceKind.Libre, StartWithWindows = false, PublishIntervalMs = 500 };
        var source = new AppSettings { SensorSource = SensorSourceKind.HwInfo, StartWithWindows = true, PublishIntervalMs = 1000 };
        source.HwInfoProfile.OverlayGroups =
        [
            SensorFixture.CreateGroup("g1", "GPU", 0, true, SensorFixture.CreateSelectedSensor("gpu-temp", "GPU", "{0:0}°C", 0))
        ];
        source.LibreProfile.OverlayGroups =
        [
            SensorFixture.CreateGroup("g2", "CPU", 0, true, SensorFixture.CreateSelectedSensor("cpu-temp", "CPU", "{0:0}°C", 0))
        ];

        target.ApplyFrom(source, preserveCurrentSensorSource: false);

        Assert.Equal(SensorSourceKind.HwInfo, target.SensorSource);
        Assert.True(target.StartWithWindows);
        Assert.Equal(1000, target.PublishIntervalMs);
        Assert.Equal("GPU", target.HwInfoProfile.OverlayGroups[0].Name);
        Assert.Equal("CPU", target.LibreProfile.OverlayGroups[0].Name);

        // Deep copy: mutating source afterward must not affect target.
        source.HwInfoProfile.OverlayGroups[0].Name = "Mutated";
        Assert.Equal("GPU", target.HwInfoProfile.OverlayGroups[0].Name);
    }

    [Fact]
    public void ApplyFrom_PreservingSensorSource_KeepsLiveSourceButAppliesRestFromSource()
    {
        var target = new AppSettings { SensorSource = SensorSourceKind.Libre, PublishIntervalMs = 500 };
        var source = new AppSettings { SensorSource = SensorSourceKind.HwInfo, PublishIntervalMs = 1000 };
        source.HwInfoProfile.OverlayGroups =
        [
            SensorFixture.CreateGroup("g1", "GPU", 0, true, SensorFixture.CreateSelectedSensor("gpu-temp", "GPU", "{0:0}°C", 0))
        ];

        target.ApplyFrom(source, preserveCurrentSensorSource: true);

        Assert.Equal(SensorSourceKind.Libre, target.SensorSource);
        Assert.Equal(1000, target.PublishIntervalMs);
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

        target.ApplyFrom(source, preserveCurrentSensorSource: false);

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
    public void ApplyFrom_PreserveSource_UsesCopiedProfileForPreservedActiveSource()
    {
        var target = new AppSettings { SensorSource = SensorSourceKind.Libre };
        var source = new AppSettings { SensorSource = SensorSourceKind.HwInfo };
        source.LibreProfile.OverlayGroups =
        [
            SensorFixture.CreateGroup("libre-group", "SourceLibre", 0, true)
        ];
        source.HwInfoProfile.OverlayGroups =
        [
            SensorFixture.CreateGroup("hwinfo-group", "SourceHwInfo", 0, true)
        ];

        target.ApplyFrom(source, preserveCurrentSensorSource: true);

        // target.SensorSource stayed Libre, so its ActiveProfile snapshot must reflect
        // source's *Libre* profile - not source's HwInfo (source's own active) profile.
        Assert.Equal(SensorSourceKind.Libre, target.SensorSource);
        Assert.Equal("SourceLibre", target.GetOverlayGroupsSnapshot()[0].Name);
    }

    [Fact]
    public void ReplaceOverlayGroups_UpdatesActiveProfileSelectedSensors()
    {
        var settings = new AppSettings { SensorSource = SensorSourceKind.HwInfo };
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
    public void ReplaceOverlayGroups_LibreSourceAlsoUpdatesLegacyTopLevelFields()
    {
        var settings = new AppSettings { SensorSource = SensorSourceKind.Libre };
        var sensor = SensorFixture.CreateSelectedSensor("libre-sensor", "Libre", "{0:0}", 0);
        var group = SensorFixture.CreateGroup("libre-group", "LibreGroup", 0, true, sensor);

        settings.ReplaceOverlayGroups([group]);

        Assert.Equal("LibreGroup", settings.OverlayGroups[0].Name);
        Assert.Single(settings.SelectedSensors);
        Assert.Equal("libre-sensor", settings.SelectedSensors[0].SensorId);
    }

    [Fact]
    public void ReplaceOverlayGroups_HwInfoSourceDoesNotTouchLegacyTopLevelFields()
    {
        var settings = new AppSettings { SensorSource = SensorSourceKind.HwInfo };
        var sensor = SensorFixture.CreateSelectedSensor("hwinfo-sensor", "HwInfo", "{0:0}", 0);
        var group = SensorFixture.CreateGroup("hwinfo-group", "HwInfoGroup", 0, true, sensor);

        settings.ReplaceOverlayGroups([group]);

        Assert.Empty(settings.OverlayGroups);
        Assert.Empty(settings.SelectedSensors);
    }

    [Fact]
    public void ReplaceOverlayGroups_NormalizesOrderFromZeroSequentially()
    {
        var settings = new AppSettings { SensorSource = SensorSourceKind.HwInfo };
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
        var settings = new AppSettings { SensorSource = SensorSourceKind.HwInfo };
        var group = SensorFixture.CreateGroup("g1", "Original", 0, true);

        settings.ReplaceOverlayGroups([group]);
        group.Name = "Mutated After Call";

        var snapshot = settings.GetOverlayGroupsSnapshot();
        Assert.Equal("Original", snapshot[0].Name);
    }

    [Fact]
    public void LibreAndHwInfoProfiles_RemainIsolatedFromEachOther()
    {
        var settings = new AppSettings { SensorSource = SensorSourceKind.Libre };
        settings.ReplaceOverlayGroups([SensorFixture.CreateGroup("libre-group", "LibreGroup", 0, true)]);

        settings.SensorSource = SensorSourceKind.HwInfo;
        settings.ReplaceOverlayGroups([SensorFixture.CreateGroup("hwinfo-group", "HwInfoGroup", 0, true)]);

        Assert.Equal("HwInfoGroup", settings.GetOverlayGroupsSnapshot()[0].Name);
        Assert.Equal("LibreGroup", settings.LibreProfile.OverlayGroups[0].Name);
        Assert.Equal("HwInfoGroup", settings.HwInfoProfile.OverlayGroups[0].Name);

        settings.SensorSource = SensorSourceKind.Libre;
        Assert.Equal("LibreGroup", settings.GetOverlayGroupsSnapshot()[0].Name);
    }
}
