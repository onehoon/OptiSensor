using System.Diagnostics;
using OptiSensor.App;
using OptiSensor.Settings;

namespace OptiSensor.Install;

internal static class AppInstaller
{
    public static bool Install(bool verbose)
    {
        if (verbose)
            Console.WriteLine($"Installing OptiSensor to: {AppPaths.InstallDirectory}");

        SimpleLog.TryWrite($"Install started. Source={AppPaths.CurrentExecutablePath}, Target={AppPaths.InstalledExecutablePath}");

        Directory.CreateDirectory(AppPaths.InstallDirectory);
        AppPaths.EnsureDataDirectories();

        if (!AppPaths.PathsEqual(AppPaths.CurrentExecutablePath, AppPaths.InstalledExecutablePath) &&
            !CopyCurrentExecutableToInstallPath(verbose))
        {
            SimpleLog.TryWrite("Install failed because the executable could not be copied.");
            return false;
        }

        var settings = AppSettings.LoadOrCreate();
        if (settings.StartWithWindows)
            StartupRegistration.Register();
        else
            StartupRegistration.Unregister();

        SimpleLog.TryWrite("Install completed.");

        if (verbose)
        {
            Console.WriteLine($"Installed executable: {AppPaths.InstalledExecutablePath}");
            Console.WriteLine($"Settings: {AppPaths.SettingsFilePath}");
            Console.WriteLine(settings.StartWithWindows
                ? "OptiSensor Task Scheduler startup task registration was requested."
                : "OptiSensor Task Scheduler startup task was not registered because startWithWindows is false.");
        }

        return true;
    }

    public static void Uninstall()
    {
        Console.WriteLine("Uninstalling OptiSensor...");
        SimpleLog.TryWrite("Uninstall started.");

        StartupRegistration.Unregister();
        Console.WriteLine("OptiSensor startup task removed.");

        TryDeleteInstalledExecutable();
        TryDeleteEmptyInstallDirectory();

        SimpleLog.TryWrite("Uninstall completed.");
        Console.WriteLine($"Settings and logs were kept at: {AppPaths.DataDirectory}");
    }

    public static bool EnsureInstalledAndRelaunchIfNeeded(bool startup)
    {
        if (AppPaths.IsRunningFromInstallDirectory())
            return false;

        var installSucceeded = true;
        if (startup && !File.Exists(AppPaths.InstalledExecutablePath))
            installSucceeded = Install(verbose: false);
        else if (!startup)
            installSucceeded = Install(verbose: true);

        if (!installSucceeded && !File.Exists(AppPaths.InstalledExecutablePath))
            throw new InvalidOperationException("OptiSensor could not install itself and no installed executable is available.");

        if (!installSucceeded)
            SimpleLog.TryWrite("Launching the existing installed executable after install copy failed.");

        var arguments = startup ? "--startup" : "";
        if (!startup)
            Console.WriteLine("Launching installed OptiSensor...");

        StartInstalled(arguments);
        return true;
    }

    private static void StartInstalled(string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.InstalledExecutablePath,
            Arguments = arguments,
            UseShellExecute = false
        });
    }

    private static bool CopyCurrentExecutableToInstallPath(bool verbose)
    {
        try
        {
            File.Copy(AppPaths.CurrentExecutablePath, AppPaths.InstalledExecutablePath, overwrite: true);
            return true;
        }
        catch (IOException ex) when (File.Exists(AppPaths.InstalledExecutablePath))
        {
            var message = $"Could not update installed executable because it is currently in use: {ex.Message}";
            SimpleLog.TryWrite(message);
            if (verbose)
                Console.WriteLine(message);

            return false;
        }
    }

    private static void TryDeleteInstalledExecutable()
    {
        if (!File.Exists(AppPaths.InstalledExecutablePath))
            return;

        try
        {
            File.Delete(AppPaths.InstalledExecutablePath);
            Console.WriteLine($"Deleted installed executable: {AppPaths.InstalledExecutablePath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("Could not delete installed executable because it is currently running or locked.");
            Console.WriteLine("You can delete it manually after closing OptiSensor:");
            Console.WriteLine(AppPaths.InstalledExecutablePath);
            SimpleLog.TryWrite($"Could not delete installed executable: {ex.Message}");
        }
    }

    private static void TryDeleteEmptyInstallDirectory()
    {
        try
        {
            if (Directory.Exists(AppPaths.InstallDirectory) &&
                !Directory.EnumerateFileSystemEntries(AppPaths.InstallDirectory).Any())
            {
                Directory.Delete(AppPaths.InstallDirectory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SimpleLog.TryWrite($"Could not remove install directory: {ex.Message}");
        }
    }
}
