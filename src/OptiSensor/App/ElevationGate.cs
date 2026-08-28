using System.Diagnostics;
using System.ComponentModel;
using System.Security.Principal;

namespace OptiSensor.App;

internal static class ElevationGate
{
    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool TryRestartElevated(string[] args)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            SimpleLog.TryWrite("Could not restart elevated: process path was unavailable.");
            return false;
        }

        try
        {
            using var process = Process.Start(CreateStartInfo(executablePath, args));
            if (process is null)
            {
                SimpleLog.TryWrite("Could not restart elevated: Process.Start returned no process.");
                return false;
            }

            SimpleLog.TryWrite("Elevated OptiSensor restart requested.");
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            SimpleLog.TryWrite("Elevated OptiSensor restart was canceled by the user.");
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            SimpleLog.TryWrite($"Could not restart elevated: {ex.Message}");
            return false;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executablePath, IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true,
            Verb = "runas"
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        return startInfo;
    }
}
