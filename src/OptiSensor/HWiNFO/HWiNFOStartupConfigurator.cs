using System.Diagnostics;
using System.ComponentModel;
using OptiSensor.Libre;

namespace OptiSensor.HWiNFO;

internal sealed record HWiNFOStartupResult(bool Success, string Message);

internal sealed record HWiNFOSharedMemoryStartupResult(bool Ready, string Message);

internal static class HWiNFOStartupConfigurator
{
    private static readonly TimeSpan SharedMemoryReadyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExistingSharedMemoryProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SharedMemoryProbeInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ElevatedHelperTimeout = TimeSpan.FromSeconds(20);
    private const string ElevatedConfigurationArgument = "--configure-hwinfo-shared-memory";

    public static string EnsureSharedMemoryEnabled()
    {
        var path = FindSettingsPath();
        if (path is null) return "HWiNFO settings file not found.";
        var lines = File.ReadAllLines(path).ToList();
        var section = -1;
        var setting = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().Equals("[Settings]", StringComparison.OrdinalIgnoreCase)) section = i;
            if (section >= 0 && i > section && lines[i].StartsWith("[", StringComparison.Ordinal)) break;
            if (section >= 0 && lines[i].TrimStart().StartsWith("SensorsSM=", StringComparison.OrdinalIgnoreCase)) setting = i;
        }
        if (setting >= 0 && lines[setting].Trim().Equals("SensorsSM=1", StringComparison.OrdinalIgnoreCase)) return "HWiNFO shared memory is already enabled.";
        if (setting >= 0) lines[setting] = "SensorsSM=1";
        else
        {
            if (section < 0) { lines.Add("[Settings]"); section = lines.Count - 1; }
            lines.Insert(section + 1, "SensorsSM=1");
        }
        File.WriteAllLines(path, lines);
        return "HWiNFO shared memory startup option enabled.";
    }

    public static HWiNFOStartupResult EnsureRunningWithSharedMemory()
    {
        var processes = Process.GetProcessesByName("HWiNFO64");
        var running = processes.FirstOrDefault();
        var executablePath = TryGetExecutablePath(running);
        var settingsPath = FindSettingsPath(executablePath);
        if (settingsPath is null)
            return new HWiNFOStartupResult(false, "HWiNFO settings file not found.");

        SetSharedMemoryEnabled(settingsPath);

        if (running is not null)
        {
            try
            {
                var closed = running.CloseMainWindow() && running.WaitForExit(5000);
                if (!closed)
                {
                    running.Kill(entireProcessTree: true);
                    if (!running.WaitForExit(5000))
                        return new HWiNFOStartupResult(false, "HWiNFO could not be closed for restart.");
                }
            }
            finally
            {
                running.Dispose();
            }
        }

        executablePath ??= FindExecutablePath();
        if (executablePath is null)
            return new HWiNFOStartupResult(false, "HWiNFO executable not found.");

        var started = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        if (started is null)
            return new HWiNFOStartupResult(false, "HWiNFO process could not be started.");

        started.Dispose();

        return new HWiNFOStartupResult(
            true,
            running is null
                ? "HWiNFO started with shared memory enabled."
                : "HWiNFO restarted with shared memory enabled.");
    }

    private static HWiNFOStartupResult EnsureRunningWithSharedMemoryElevated()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return new HWiNFOStartupResult(false, "OptiSensor executable was not found for elevated HWiNFO configuration.");

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = ElevatedConfigurationArgument,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (process is null)
                return new HWiNFOStartupResult(false, "Elevated HWiNFO configuration could not be started.");

            if (!process.WaitForExit((int)ElevatedHelperTimeout.TotalMilliseconds))
                return new HWiNFOStartupResult(false, "Elevated HWiNFO configuration timed out.");

            return process.ExitCode == 0
                ? new HWiNFOStartupResult(true, "HWiNFO shared memory configuration completed with administrator rights.")
                : new HWiNFOStartupResult(false, $"Elevated HWiNFO configuration failed with exit code {process.ExitCode}.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new HWiNFOStartupResult(false, "Administrator permission was canceled for HWiNFO shared memory configuration.");
        }
        catch (Win32Exception ex)
        {
            return new HWiNFOStartupResult(false, $"Elevated HWiNFO configuration could not start. Win32Error={ex.NativeErrorCode}.");
        }
    }

    public static async Task<HWiNFOSharedMemoryStartupResult> EnsureRunningAndWaitForSharedMemoryAsync(CancellationToken cancellationToken)
    {
        if (IsHWiNFO64Running())
        {
            var existingSharedMemory = await WaitForSharedMemoryAsync(ExistingSharedMemoryProbeTimeout, cancellationToken).ConfigureAwait(false);
            if (existingSharedMemory.Ready)
            {
                return new HWiNFOSharedMemoryStartupResult(
                    true,
                    "HWiNFO shared memory is already ready; restart skipped.");
            }
        }

        HWiNFOStartupResult startup;
        try
        {
            startup = await Task.Run(EnsureRunningWithSharedMemoryElevated, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new HWiNFOSharedMemoryStartupResult(false, $"HWiNFO startup failed: {ex.Message}");
        }

        if (!startup.Success)
            return new HWiNFOSharedMemoryStartupResult(false, startup.Message);

        return await WaitForSharedMemoryAsync(SharedMemoryReadyTimeout, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<HWiNFOSharedMemoryStartupResult> WaitForSharedMemoryAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? lastReadException = null;
        using var reader = new HwInfoSensorReader();

        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var snapshot = reader.ReadSnapshot();
                if (snapshot.Sensors.Count > 0)
                {
                    return new HWiNFOSharedMemoryStartupResult(
                        true,
                        $"HWiNFO shared memory became ready after {stopwatch.ElapsedMilliseconds} ms.");
                }
            }
            catch (Exception ex)
            {
                lastReadException = ex;
            }

            await Task.Delay(SharedMemoryProbeInterval, cancellationToken).ConfigureAwait(false);
        }

        var detail = lastReadException is null ? string.Empty : $" Last error: {lastReadException.Message}";
        return new HWiNFOSharedMemoryStartupResult(
            false,
            $"HWiNFO shared memory did not become ready within {timeout.TotalSeconds:0} seconds.{detail}");
    }

    private static bool IsHWiNFO64Running()
    {
        var processes = Process.GetProcessesByName("HWiNFO64");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private static void SetSharedMemoryEnabled(string path)
    {
        var lines = File.ReadAllLines(path).ToList();
        var section = -1;
        var setting = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().Equals("[Settings]", StringComparison.OrdinalIgnoreCase)) section = i;
            if (section >= 0 && i > section && lines[i].StartsWith("[", StringComparison.Ordinal)) break;
            if (section >= 0 && lines[i].TrimStart().StartsWith("SensorsSM=", StringComparison.OrdinalIgnoreCase)) setting = i;
        }
        if (setting >= 0) lines[setting] = "SensorsSM=1";
        else
        {
            if (section < 0) { lines.Add("[Settings]"); section = lines.Count - 1; }
            lines.Insert(section + 1, "SensorsSM=1");
        }
        File.WriteAllLines(path, lines);
    }

    private static string? FindSettingsPath(string? executablePath = null)
    {
        var candidates = new[]
        {
            executablePath is null ? null : Path.ChangeExtension(executablePath, ".INI"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HWiNFO", "HWiNFO64.INI"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "HWiNFO64", "HWiNFO64.INI"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "HWiNFO64", "HWiNFO64.INI")
        };
        return candidates.FirstOrDefault(path => path is not null && File.Exists(path));
    }

    private static string? FindExecutablePath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "HWiNFO64", "HWiNFO64.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "HWiNFO64", "HWiNFO64.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? TryGetExecutablePath(Process? process)
    {
        try { return process?.MainModule?.FileName; }
        catch (System.ComponentModel.Win32Exception) { return null; }
        catch (InvalidOperationException) { return null; }
    }
}
