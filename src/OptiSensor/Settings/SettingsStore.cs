using System.Text.Json;
using OptiSensor.App;
using OptiSensor.Install;

namespace OptiSensor.Settings;

internal static class SettingsStore
{
    public static AppSettings LoadOrCreate()
    {
        AppPaths.EnsureDataDirectories();
        return LoadOrCreate(AppPaths.SettingsFilePath);
    }

    public static void Save(AppSettings settings)
    {
        AppPaths.EnsureDataDirectories();
        Save(settings, AppPaths.SettingsFilePath);
    }

    internal static AppSettings LoadOrCreate(string settingsFilePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath)!);

        if (!File.Exists(settingsFilePath))
            return CreateDefaults(settingsFilePath);

        try
        {
            return AppSettings.Deserialize(File.ReadAllText(settingsFilePath));
        }
        catch (JsonException)
        {
            // Invalid JSON content: keep the bad file for inspection and start from defaults.
            BackupInvalidSettings(settingsFilePath);
            return CreateDefaults(settingsFilePath);
        }
        catch (IOException ex)
        {
            // A sharing violation or transient disk / antivirus interference does not prove the
            // settings are corrupt. Leave the file untouched and let the startup error policy
            // decide - never rename or overwrite it as if the content were invalid.
            SimpleLog.TryWrite($"Failed to read settings: {ex.Message}");
            throw;
        }
    }

    internal static void Save(AppSettings settings, string settingsFilePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath)!);
        var tempPath = $"{settingsFilePath}.tmp";
        File.WriteAllText(tempPath, AppSettings.Serialize(settings));
        File.Move(tempPath, settingsFilePath, overwrite: true);
    }

    private static AppSettings CreateDefaults(string settingsFilePath)
    {
        var defaults = new AppSettings();
        Save(defaults, settingsFilePath);
        return defaults;
    }

    private static void BackupInvalidSettings(string settingsFilePath)
    {
        if (!File.Exists(settingsFilePath))
            return;

        var backupPath = $"{settingsFilePath}.bad.{DateTime.Now:yyyyMMdd-HHmmss}";
        try
        {
            File.Move(settingsFilePath, backupPath, overwrite: true);
        }
        catch (IOException ex)
        {
            // Best effort: if the invalid file can't be moved aside, log it and let CreateDefaults
            // overwrite it. Don't turn one parse failure into an uncontrolled move exception.
            SimpleLog.TryWrite($"Could not back up invalid settings file: {ex.Message}");
        }
    }
}
