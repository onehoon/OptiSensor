using OptiSensor.Install;

namespace OptiSensor.Tweaks.IntelVrr;

/// <summary>
/// Writes one detailed diagnostic log per Intel VRR Range Fix run. The file is replaced
/// (overwritten) on every run rather than accumulated - only the most recent run's diagnostics
/// are ever kept on disk.
/// </summary>
internal static class IntelVrrRunLogger
{
    private static string FilePath => Path.Combine(AppPaths.LogsDirectory, "tweaks-intel-vrr-last-run.log");

    public static void WriteRun(IEnumerable<string> lines)
    {
        try
        {
            AppPaths.EnsureDataDirectories();
            var content = string.Join(Environment.NewLine,
                new[] { $"OptiSensor Intel VRR Range Fix run - {DateTimeOffset.UtcNow:O}" }.Concat(lines));

            // Overwrite (not append): this is a single-run snapshot, not an accumulating log.
            File.WriteAllText(FilePath, content);
        }
        catch (Exception)
        {
            // Diagnostic logging is best-effort only.
        }
    }
}
