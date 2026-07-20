using System.Text.Json;
using OptiSensor.Install;
using OptiSensor.Models;

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
            settings.PublishIntervalMs = Math.Clamp(settings.PublishIntervalMs, 100, 2000);

            var selectedSource = settings.SensorSource;
            settings.SensorSource = SensorSourceKind.Libre;

            if (settings.OverlayGroups.Count > 0)
            {
                settings.ReplaceOverlayGroups(settings.OverlayGroups);
            }
            else
            {
                // Legacy migration path: older files may only contain selectedSensors.
                settings.ReplaceSelectedSensors(settings.SelectedSensors ?? []);
            }

            settings.ReplaceSensorCategoryFilters(settings.SensorCategoryFilters);
            settings.SensorSource = selectedSource;
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
        var tempPath = $"{AppPaths.SettingsFilePath}.tmp";
        File.WriteAllText(tempPath, AppSettings.Serialize(settings));
        File.Move(tempPath, AppPaths.SettingsFilePath, overwrite: true);
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
