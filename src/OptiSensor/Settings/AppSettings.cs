using System.Text.Json;
using System.Text.Json.Serialization;
using OptiSensor.Install;

namespace OptiSensor.Settings;

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
        return SettingsStore.LoadOrCreate();
    }

    public void Save()
    {
        SettingsStore.Save(this);
    }

    internal static AppSettings Deserialize(string json)
    {
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    internal static string Serialize(AppSettings settings)
    {
        return JsonSerializer.Serialize(settings, JsonOptions);
    }
}
