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
            return AppSettings.Deserialize(json);
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
