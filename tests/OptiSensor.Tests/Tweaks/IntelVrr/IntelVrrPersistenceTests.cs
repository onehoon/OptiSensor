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
    public void RunLogger_MultipleAttemptsInOneSession_AreAllPreserved()
    {
        var logPath = Path.Combine(AppPaths.LogsDirectory, "tweaks-intel-vrr-last-run.log");

        IntelVrrRunLogger.StartSession();
        IntelVrrRunLogger.AppendAttempt(1, ["first attempt line one", "first attempt line two"]);
        IntelVrrRunLogger.AppendAttempt(2, ["second attempt line only"]);

        Assert.True(File.Exists(logPath));
        var content = File.ReadAllText(logPath);

        // Both attempts within the SAME session must be present - retries must not overwrite
        // earlier attempts' diagnostics.
        Assert.Contains("first attempt line one", content);
        Assert.Contains("first attempt line two", content);
        Assert.Contains("second attempt line only", content);
        Assert.Contains("Attempt 1", content);
        Assert.Contains("Attempt 2", content);
    }

    [Fact]
    public void RunLogger_NewSession_ReplacesPreviousSessionsLog()
    {
        var logPath = Path.Combine(AppPaths.LogsDirectory, "tweaks-intel-vrr-last-run.log");

        IntelVrrRunLogger.StartSession();
        IntelVrrRunLogger.AppendAttempt(1, ["previous session attempt line"]);

        // A new process launch's tweak sequence starts here - only this call point should ever
        // truncate the file, not each retry attempt.
        IntelVrrRunLogger.StartSession();
        var freshSessionContent = File.ReadAllText(logPath);
        Assert.DoesNotContain("previous session attempt line", freshSessionContent);

        IntelVrrRunLogger.AppendAttempt(1, ["new session attempt line"]);
        var finalContent = File.ReadAllText(logPath);
        Assert.Contains("new session attempt line", finalContent);
        Assert.DoesNotContain("previous session attempt line", finalContent);
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
