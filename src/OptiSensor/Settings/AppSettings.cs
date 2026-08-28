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

    [JsonPropertyName("hwInfoProfile")]
    public SensorSourceProfile HwInfoProfile { get; set; } = new();

    [JsonPropertyName("selectedSensors")]
    public List<SelectedOverlaySensor> SelectedSensors { get; set; } = [];

    [JsonPropertyName("overlayGroups")]
    public List<OverlayGroup> OverlayGroups { get; set; } = [];

    [JsonPropertyName("sensorCategoryFilters")]
    public Dictionary<OptiSensorCategory, bool> SensorCategoryFilters { get; set; } = [];

    /// <summary>Whether the Intel VRR Range Fix tweak should run at next startup. Toggling this
    /// only persists the flag - it never triggers the tweak itself.</summary>
    [JsonPropertyName("intelVrrRangeFixEnabled")]
    public bool IntelVrrRangeFixEnabled { get; set; }

    [JsonIgnore]
    public IReadOnlyList<SelectedOverlaySensor> EnabledSelectedSensors =>
        GetEnabledSelectedSensorsSnapshot();

    [JsonIgnore]
    public SensorSourceProfile ActiveProfile => HwInfoProfile;

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
            normalized[category] = ActiveProfile.SensorCategoryFilters.TryGetValue(category, out var isChecked) ? isChecked : true;

        return normalized;
    }

    public IReadOnlyDictionary<OptiSensorCategory, bool> GetActiveSensorCategoryFilterSnapshot()
    {
        var filters = ActiveProfile.SensorCategoryFilters;
        return Enum.GetValues<OptiSensorCategory>().ToDictionary(category => category,
            category => !filters.TryGetValue(category, out var value) || value);
    }

    public void ReplaceSensorCategoryFilters(IEnumerable<KeyValuePair<OptiSensorCategory, bool>> filters)
    {
        var normalized = new Dictionary<OptiSensorCategory, bool>();
        foreach (var category in Enum.GetValues<OptiSensorCategory>())
            normalized[category] = true;

        foreach (var filter in filters)
            normalized[filter.Key] = filter.Value;

        ActiveProfile.SensorCategoryFilters = normalized;
    }

    public IReadOnlyList<OverlayGroup> GetOverlayGroupsSnapshot()
    {
        lock (_overlayGroupsLock)
        {
            return ActiveProfile.OverlayGroups
                .OrderBy(group => group.Order)
                .Select(group => group.Copy())
                .ToArray();
        }
    }

    public void ReplaceOverlayGroups(IEnumerable<OverlayGroup> groups)
    {
        lock (_overlayGroupsLock)
        {
            var normalizedGroups = groups
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
            ActiveProfile.OverlayGroups = normalizedGroups;
            ActiveProfile.SelectedSensors = normalizedGroups.SelectMany(group => group.Sensors).Select(sensor => sensor.Copy()).ToList();
        }
    }

    public AppSettings CreateCopy()
    {
        return Deserialize(Serialize(this));
    }

    public void ApplyFrom(AppSettings source)
    {
        lock (_overlayGroupsLock)
        {
            lock (_selectedSensorsLock)
            {
                StartWithWindows = source.StartWithWindows;
                StartMinimized = source.StartMinimized;
                HwInfoProfile = CopyProfile(source.HwInfoProfile);
                SelectedSensors = source.SelectedSensors.Select(sensor => sensor.Copy()).ToList();
                OverlayGroups = source.OverlayGroups.Select(group => group.Copy()).ToList();
                SensorCategoryFilters = new Dictionary<OptiSensorCategory, bool>(source.SensorCategoryFilters);
            }
        }
    }

    private static SensorSourceProfile CopyProfile(SensorSourceProfile profile)
    {
        return new SensorSourceProfile
        {
            SelectedSensors = profile.SelectedSensors.Select(sensor => sensor.Copy()).ToList(),
            OverlayGroups = profile.OverlayGroups.Select(group => group.Copy()).ToList(),
            SensorCategoryFilters = new Dictionary<OptiSensorCategory, bool>(profile.SensorCategoryFilters)
        };
    }

    internal static AppSettings Deserialize(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        settings.SelectedSensors ??= [];
        settings.OverlayGroups ??= [];
        settings.SensorCategoryFilters ??= [];
        settings.HwInfoProfile ??= new();
        NormalizeProfile(settings.HwInfoProfile);
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

    private static void NormalizeProfile(SensorSourceProfile profile)
    {
        profile.SelectedSensors ??= [];
        profile.OverlayGroups ??= [];
        profile.SensorCategoryFilters ??= [];
    }

    internal static string Serialize(AppSettings settings)
    {
        return JsonSerializer.Serialize(settings, JsonOptions);
    }
}
