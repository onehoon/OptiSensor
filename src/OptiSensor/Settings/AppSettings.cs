using System.Text.Json;
using System.Text.Json.Serialization;
using OptiSensor.Install;
using OptiSensor.Models;

namespace OptiSensor.Settings;

internal sealed class AppSettings
{
    private readonly object _selectedSensorsLock = new();
    private readonly object _overlayGroupsLock = new();

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

    [JsonPropertyName("overlayGroups")]
    public List<OverlayGroup> OverlayGroups { get; set; } = [];

    [JsonPropertyName("sensorCategoryFilters")]
    public Dictionary<OptiSensorCategory, bool> SensorCategoryFilters { get; set; } = [];

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
        return GetOverlayGroupsSnapshot()
            .SelectMany(group => group.Sensors)
            .OrderBy(sensor => sensor.Order)
            .Select(sensor => sensor.Copy())
            .ToArray();
    }

    public IReadOnlyList<SelectedOverlaySensor> GetEnabledSelectedSensorsSnapshot()
    {
        return GetSelectedSensorsSnapshot()
            .Where(sensor => sensor.Enabled)
            .ToArray();
    }

    public void ReplaceSelectedSensors(IEnumerable<SelectedOverlaySensor> sensors)
    {
        ReplaceOverlayGroups(
        [
            new OverlayGroup
            {
                Id = "default",
                Name = string.Empty,
                Order = 0,
                Enabled = true,
                Sensors = sensors.ToList()
            }
        ]);
    }

    public IReadOnlyDictionary<OptiSensorCategory, bool> GetSensorCategoryFilterSnapshot()
    {
        var normalized = new Dictionary<OptiSensorCategory, bool>();
        foreach (var category in Enum.GetValues<OptiSensorCategory>())
            normalized[category] = SensorCategoryFilters.TryGetValue(category, out var isChecked) ? isChecked : true;

        return normalized;
    }

    public void ReplaceSensorCategoryFilters(IEnumerable<KeyValuePair<OptiSensorCategory, bool>> filters)
    {
        var normalized = new Dictionary<OptiSensorCategory, bool>();
        foreach (var category in Enum.GetValues<OptiSensorCategory>())
            normalized[category] = true;

        foreach (var filter in filters)
            normalized[filter.Key] = filter.Value;

        SensorCategoryFilters = normalized;
    }

    public IReadOnlyList<OverlayGroup> GetOverlayGroupsSnapshot()
    {
        lock (_overlayGroupsLock)
        {
            return OverlayGroups
                .OrderBy(group => group.Order)
                .Select(group => group.Copy())
                .ToArray();
        }
    }

    public void ReplaceOverlayGroups(IEnumerable<OverlayGroup> groups)
    {
        lock (_overlayGroupsLock)
        {
            OverlayGroups = groups
                .OrderBy(group => group.Order)
                .Select((group, groupIndex) =>
                {
                    var clone = group.Copy();
                    clone.Order = groupIndex;
                    clone.Sensors = clone.Sensors
                        .OrderBy(sensor => sensor.Order)
                        .Select((sensor, sensorIndex) =>
                        {
                            var sensorClone = sensor.Copy();
                            sensorClone.Order = sensorIndex;
                            return sensorClone;
                        })
                        .ToList();
                    return clone;
                })
                .ToList();

            lock (_selectedSensorsLock)
            {
                SelectedSensors = OverlayGroups.SelectMany(group => group.Sensors).Select(sensor => sensor.Copy()).ToList();
            }
        }
    }

    internal static AppSettings Deserialize(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        settings.SelectedSensors ??= [];
        settings.OverlayGroups ??= [];
        settings.SensorCategoryFilters ??= [];
        if (settings.OverlayGroups.Count == 0 && settings.SelectedSensors.Count > 0)
        {
            settings.OverlayGroups =
            [
                new OverlayGroup
                {
                    Id = "default",
                    Name = string.Empty,
                    Order = 0,
                    Enabled = true,
                    Sensors = settings.SelectedSensors
                }
            ];
        }

        return settings;
    }

    internal static string Serialize(AppSettings settings)
    {
        return JsonSerializer.Serialize(settings, JsonOptions);
    }
}
