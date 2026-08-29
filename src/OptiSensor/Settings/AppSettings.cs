using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptiSensor.Settings;

/// <summary>
/// The Claw edition persists only two flags: whether OptiSensor launches at sign-in, and whether
/// the Intel VRR Range Fix should run. The native telemetry line is fixed by
/// <c>ClawTelemetryFormatter</c>, so there is no sensor-selection state. Unknown legacy JSON keys
/// (old HWiNFO/overlay-group settings) are ignored.
/// </summary>
internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Whether the Intel VRR Range Fix tweak should run at next startup. Toggling this
    /// only persists the flag - it never triggers the tweak itself.</summary>
    [JsonPropertyName("intelVrrRangeFixEnabled")]
    public bool IntelVrrRangeFixEnabled { get; set; }

    public static AppSettings LoadOrCreate() => SettingsStore.LoadOrCreate();

    public void Save() => SettingsStore.Save(this);

    internal static AppSettings Deserialize(string json) =>
        JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

    internal static string Serialize(AppSettings settings) =>
        JsonSerializer.Serialize(settings, JsonOptions);
}
