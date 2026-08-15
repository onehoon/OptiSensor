using OptiSensor.Install;
using OptiSensor.Tweaks.IntelVrr;
using Xunit;

namespace OptiSensor.Tests.Tweaks.IntelVrr;

public class IntelVrrPersistenceTests
{
    [Fact]
    public void ResultStore_SaveThenLoad_RoundTripsExpectedCompactState()
    {
        var result = IntelVrrRunResult.Create(
            IntelVrrRunStatus.Applied,
            "Restored the native VRR range.",
            panelName: "PN8007QB1-2",
            rangeBeforeText: "60-120 Hz",
            rangeAfterText: "48-120 Hz");

        IntelVrrResultStore.Save(result);
        var loaded = IntelVrrResultStore.TryLoad();

        Assert.NotNull(loaded);
        Assert.Equal(IntelVrrRunStatus.Applied, loaded!.Status);
        Assert.Equal("PN8007QB1-2", loaded.PanelName);
        Assert.Equal("60-120 Hz", loaded.RangeBeforeText);
        Assert.Equal("48-120 Hz", loaded.RangeAfterText);
        Assert.Equal("Restored the native VRR range.", loaded.Message);
    }

    [Fact]
    public void RunLogger_SecondRunReplacesFirst_NotAccumulated()
    {
        IntelVrrRunLogger.WriteRun(["first run line one", "first run line two"]);
        var logPath = Path.Combine(AppPaths.LogsDirectory, "tweaks-intel-vrr-last-run.log");
        Assert.True(File.Exists(logPath));
        var firstContent = File.ReadAllText(logPath);
        Assert.Contains("first run line one", firstContent);

        IntelVrrRunLogger.WriteRun(["second run line only"]);
        var secondContent = File.ReadAllText(logPath);

        Assert.Contains("second run line only", secondContent);
        Assert.DoesNotContain("first run line one", secondContent);
        Assert.DoesNotContain("first run line two", secondContent);
    }

    [Fact]
    public void ToggleSetting_SavesImmediately_WithoutSeparateSaveButton()
    {
        var settings = new OptiSensor.Settings.AppSettings { IntelVrrRangeFixEnabled = false };

        settings.IntelVrrRangeFixEnabled = true;
        settings.Save();

        var reloaded = OptiSensor.Settings.AppSettings.Deserialize(OptiSensor.Settings.AppSettings.Serialize(settings));
        Assert.True(reloaded.IntelVrrRangeFixEnabled);

        // Also verify it round-trips through the actual on-disk settings file, mirroring how the
        // TweaksPage toggle persists without requiring a separate "Save" action elsewhere.
        var fromDisk = OptiSensor.Settings.AppSettings.LoadOrCreate();
        Assert.True(fromDisk.IntelVrrRangeFixEnabled);
    }
}
