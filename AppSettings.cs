using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptiSensor;

internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; } = true;

    [JsonPropertyName("startMinimized")]
    public bool StartMinimized { get; set; } = true;

    [JsonPropertyName("publishIntervalMs")]
    public int PublishIntervalMs { get; set; } = 1000;

    [JsonIgnore]
    public int ClampedPublishIntervalMs => Math.Clamp(PublishIntervalMs, 100, 10000);

    public static AppSettings LoadOrCreate()
    {
        AppPaths.EnsureDataDirectories();

        if (!File.Exists(AppPaths.SettingsFilePath))
        {
            var defaults = new AppSettings();
            defaults.Save();
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(AppPaths.SettingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.PublishIntervalMs = Math.Clamp(settings.PublishIntervalMs, 100, 10000);
            settings.Save();
            return settings;
        }
        catch (JsonException)
        {
            BackupInvalidSettings();
            var defaults = new AppSettings();
            defaults.Save();
            return defaults;
        }
        catch (IOException)
        {
            BackupInvalidSettings();
            var defaults = new AppSettings();
            defaults.Save();
            return defaults;
        }
    }

    public void Save()
    {
        AppPaths.EnsureDataDirectories();
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(AppPaths.SettingsFilePath, json);
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
