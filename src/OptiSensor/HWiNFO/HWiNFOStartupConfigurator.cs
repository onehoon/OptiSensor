using Microsoft.Win32;

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

    private static string? FindSettingsPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HWiNFO", "HWiNFO64.INI"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "HWiNFO64", "HWiNFO64.INI"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "HWiNFO64", "HWiNFO64.INI")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
