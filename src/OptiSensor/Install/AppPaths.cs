using System.Diagnostics;

namespace OptiSensor.Install;

internal static class AppPaths
{
    public const string AppName = "OptiSensor";
    public const string ExecutableName = "OptiSensor.exe";

    public static string LocalAppData { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

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

    public static bool IsRunningFromVelopackCurrentDirectory
    {
        get
        {
            var directory = Path.GetDirectoryName(CurrentExecutablePath);
            return directory is not null &&
                   string.Equals(Path.GetFileName(directory), "current", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void EnsureDataDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
