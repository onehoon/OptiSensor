using System.Diagnostics;

namespace OptiSensor.HWiNFO;

internal static class HWiNFOStartupConfigurator
{
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

    public static string EnsureRunningWithSharedMemory()
    {
        var processes = Process.GetProcessesByName("HWiNFO64");
        var running = processes.FirstOrDefault();
        var executablePath = TryGetExecutablePath(running);
        var settingsPath = FindSettingsPath(executablePath);
        if (settingsPath is null)
            return "HWiNFO settings file not found.";

        SetSharedMemoryEnabled(settingsPath);

        if (running is not null)
        {
            try
            {
                running.CloseMainWindow();
                if (!running.WaitForExit(5000))
                    return "HWiNFO could not be closed for restart.";
            }
            finally
            {
                running.Dispose();
            }
        }

        executablePath ??= FindExecutablePath();
        if (executablePath is null)
            return "HWiNFO executable not found.";

        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        return running is null
            ? "HWiNFO started with shared memory enabled."
            : "HWiNFO restarted with shared memory enabled.";
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
