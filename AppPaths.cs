using System.Diagnostics;

namespace OptiSensor;

internal static class AppPaths
{
    public const string AppName = "OptiSensor";
    public const string ExecutableName = "OptiSensor.exe";

    public static string LocalAppData { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string InstallDirectory { get; } =
        Path.Combine(LocalAppData, "Programs", AppName);

    public static string InstalledExecutablePath { get; } =
        Path.Combine(InstallDirectory, ExecutableName);

    public static string DataDirectory { get; } =
        Path.Combine(LocalAppData, AppName);

    public static string SettingsFilePath { get; } =
        Path.Combine(DataDirectory, "settings.json");

    public static string LogsDirectory { get; } =
        Path.Combine(DataDirectory, "logs");

    public static string LogFilePath { get; } =
        Path.Combine(LogsDirectory, "optisensor.log");

    public static string CurrentExecutablePath =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? throw new InvalidOperationException("Could not resolve current executable path.");

    public static bool IsRunningFromInstallDirectory()
    {
        return PathsEqual(CurrentExecutablePath, InstalledExecutablePath);
    }

    public static bool PathsEqual(string left, string right)
    {
        var fullLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return StringComparer.OrdinalIgnoreCase.Equals(fullLeft, fullRight);
    }

    public static void EnsureDataDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
