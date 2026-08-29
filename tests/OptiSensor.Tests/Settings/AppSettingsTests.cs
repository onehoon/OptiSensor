using OptiSensor.Settings;
using Xunit;

namespace OptiSensor.Tests.Settings;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_AreStartWithWindowsOnAndVrrOff()
    {
        var settings = new AppSettings();

        Assert.True(settings.StartWithWindows);
        Assert.False(settings.IntelVrrRangeFixEnabled);
    }

    [Fact]
    public void SerializeRoundTrip_PreservesTheTwoFlags()
    {
        var original = new AppSettings { StartWithWindows = false, IntelVrrRangeFixEnabled = true };

        var reloaded = AppSettings.Deserialize(AppSettings.Serialize(original));

        Assert.False(reloaded.StartWithWindows);
        Assert.True(reloaded.IntelVrrRangeFixEnabled);
    }

    [Fact]
    public void Deserialize_IgnoresObsoleteLegacyKeysWithoutThrowing()
    {
        const string legacyJson = """
        {
          "startWithWindows": false,
          "intelVrrRangeFixEnabled": true,
          "publishIntervalMs": 1500,
          "hwInfoProfile": { "overlayGroups": [], "selectedSensors": [] },
          "overlayGroups": [ { "id": "g1", "sensors": [] } ],
          "sensorCategoryFilters": { "Gpu": true }
        }
        """;

        var settings = AppSettings.Deserialize(legacyJson);

        Assert.False(settings.StartWithWindows);
        Assert.True(settings.IntelVrrRangeFixEnabled);
    }
}
