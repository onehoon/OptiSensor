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

    /// <summary>
    /// Overrides the base directory the store reads/writes under. Production callers never set
    /// this (it defaults to <see cref="AppPaths.DataDirectory"/>); tests use it to isolate their
    /// file I/O to a unique per-test temp directory instead of racing on the shared real data
    /// directory that other tests/production instances may also be writing to.
    /// </summary>
    internal static string? DataDirectoryOverride { get; set; }

    private static string DataDirectory => DataDirectoryOverride ?? AppPaths.DataDirectory;

    private static string FilePath => Path.Combine(DataDirectory, "tweaks-intel-vrr-result.json");

    public static void Save(IntelVrrRunResult result)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
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
