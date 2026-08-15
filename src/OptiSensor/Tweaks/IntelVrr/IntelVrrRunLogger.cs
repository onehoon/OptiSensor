using OptiSensor.Install;

namespace OptiSensor.Tweaks.IntelVrr;

/// <summary>
/// Writes one accumulating diagnostic log per OptiSensor startup session. The file is truncated
/// exactly once, at the start of a new process launch's tweak sequence (<see cref="StartSession"/>),
/// and every attempt made during that same session (e.g. the bounded IGCL-availability retry loop
/// in <c>TweakStartupCoordinator</c>) is appended to it, clearly demarcated, so earlier failed
/// attempts within one startup are never lost. Only the PREVIOUS startup's log is ever replaced.
/// </summary>
internal static class IntelVrrRunLogger
{
    private static readonly object Lock = new();

    private static string FilePath => Path.Combine(AppPaths.LogsDirectory, "tweaks-intel-vrr-last-run.log");

    /// <summary>Starts a new startup session: truncates any log left over from a previous process
    /// launch. Must be called exactly once, before the first attempt of this process's tweak
    /// sequence is appended - never once per retry attempt.</summary>
    public static void StartSession()
    {
        try
        {
            AppPaths.EnsureDataDirectories();
            lock (Lock)
            {
                File.WriteAllText(FilePath,
                    $"OptiSensor Intel VRR Range Fix - startup session {DateTimeOffset.UtcNow:O}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Diagnostic logging is best-effort only.
        }
    }

    /// <summary>Appends one attempt's diagnostic lines to the current session's log file. Does NOT
    /// truncate - multiple attempts within the same session accumulate in the same file.</summary>
    public static void AppendAttempt(int attemptNumber, IEnumerable<string> lines)
    {
        try
        {
            AppPaths.EnsureDataDirectories();
            var content = string.Join(Environment.NewLine,
                new[] { string.Empty, $"--- Attempt {attemptNumber} ({DateTimeOffset.UtcNow:O}) ---" }.Concat(lines));

            lock (Lock)
            {
                File.AppendAllText(FilePath, content + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // Diagnostic logging is best-effort only.
        }
    }
}
