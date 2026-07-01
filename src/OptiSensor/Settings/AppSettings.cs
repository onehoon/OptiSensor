using System.Text.Json;
using System.Text.Json.Serialization;
using OptiSensor.Install;
using OptiSensor.Models;

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

    [JsonPropertyName("selectedSensors")]
    public List<SelectedOverlaySensor> SelectedSensors { get; set; } = [];

    [JsonIgnore]
    public int ClampedPublishIntervalMs => Math.Clamp(PublishIntervalMs, 100, 10000);

    [JsonIgnore]
    public IReadOnlyList<SelectedOverlaySensor> EnabledSelectedSensors =>
        SelectedSensors.Where(sensor => sensor.Enabled).OrderBy(sensor => sensor.Order).ToArray();

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
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        settings.SelectedSensors ??= [];
        return settings;
    }

    internal static string Serialize(AppSettings settings)
    {
        return JsonSerializer.Serialize(settings, JsonOptions);
    }
}
