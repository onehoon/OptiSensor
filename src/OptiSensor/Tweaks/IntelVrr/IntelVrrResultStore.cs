using System.Text.Json;
using System.Text.Json.Serialization;
using OptiSensor.Install;

namespace OptiSensor.Tweaks.IntelVrr;

/// <summary>
/// Persists/reads the compact <see cref="IntelVrrRunResult"/> snapshot the UI reads. This is a
/// separate file/section from <see cref="IntelVrrRunLogger"/>'s detailed diagnostic log - the UI
/// must never need to parse the detailed log.
/// </summary>
internal static class IntelVrrResultStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string FilePath => Path.Combine(AppPaths.DataDirectory, "tweaks-intel-vrr-result.json");

    public static void Save(IntelVrrRunResult result)
    {
        try
        {
            AppPaths.EnsureDataDirectories();
            var tempPath = $"{FilePath}.tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(result, JsonOptions));
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch (Exception)
        {
            // Persisting the UI snapshot is best-effort - a failure here must never take down
            // the tweak run itself.
        }
    }

    public static IntelVrrRunResult? TryLoad()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<IntelVrrRunResult>(json, JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
