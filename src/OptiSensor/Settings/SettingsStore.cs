using System.Text.Json;
using OptiSensor.Install;

namespace OptiSensor.Settings;

internal static class SettingsStore
{
    public static AppSettings LoadOrCreate()
    {
        AppPaths.EnsureDataDirectories();

        if (!File.Exists(AppPaths.SettingsFilePath))
            return CreateDefaults();

        try
        {
            var json = File.ReadAllText(AppPaths.SettingsFilePath);
            var settings = AppSettings.Deserialize(json);
            settings.PublishIntervalMs = Math.Clamp(settings.PublishIntervalMs, 100, 10000);
            settings.ReplaceSelectedSensors(settings.SelectedSensors ?? []);
            Save(settings);
            return settings;
        }
        catch (JsonException)
        {
            BackupInvalidSettings();
            return CreateDefaults();
        }
        catch (IOException)
        {
            BackupInvalidSettings();
            return CreateDefaults();
        }
    }

    public static void Save(AppSettings settings)
    {
        AppPaths.EnsureDataDirectories();
        File.WriteAllText(AppPaths.SettingsFilePath, AppSettings.Serialize(settings));
    }

    private static AppSettings CreateDefaults()
    {
        var defaults = new AppSettings();
        Save(defaults);
        return defaults;
    }

    private static void BackupInvalidSettings()
    {
        if (!File.Exists(AppPaths.SettingsFilePath))
            return;

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = $"{AppPaths.SettingsFilePath}.bad.{timestamp}";
        File.Move(AppPaths.SettingsFilePath, backupPath, overwrite: true);
    }
}
