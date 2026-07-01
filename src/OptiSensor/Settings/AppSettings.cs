using System.Text.Json;
using System.Text.Json.Serialization;
using OptiSensor.Install;
using OptiSensor.Models;

namespace OptiSensor.Settings;

internal sealed class AppSettings
{
    private readonly object _selectedSensorsLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
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
        GetEnabledSelectedSensorsSnapshot();

    public static AppSettings LoadOrCreate()
    {
        return SettingsStore.LoadOrCreate();
    }

    public void Save()
    {
        SettingsStore.Save(this);
    }

    public IReadOnlyList<SelectedOverlaySensor> GetSelectedSensorsSnapshot()
    {
        lock (_selectedSensorsLock)
        {
            return SelectedSensors
                .OrderBy(sensor => sensor.Order)
                .Select(sensor => sensor.Copy())
                .ToArray();
        }
    }

    public IReadOnlyList<SelectedOverlaySensor> GetEnabledSelectedSensorsSnapshot()
    {
        return GetSelectedSensorsSnapshot()
            .Where(sensor => sensor.Enabled)
            .ToArray();
    }

    public void ReplaceSelectedSensors(IEnumerable<SelectedOverlaySensor> sensors)
    {
        lock (_selectedSensorsLock)
        {
            SelectedSensors = sensors
                .OrderBy(sensor => sensor.Order)
                .Select((sensor, index) =>
                {
                    var clone = sensor.Copy();
                    clone.Order = index;
                    return clone;
                })
                .ToList();
        }
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
