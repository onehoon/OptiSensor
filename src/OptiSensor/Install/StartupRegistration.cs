using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
using OptiSensor.App;

namespace OptiSensor.Install;

internal static class StartupRegistration
{
    private const string TaskName = "OptiSensor";
    private const string StartupArgument = "--startup";
    private const int RestartCount = 3;
    private const int SchTasksTimeoutMs = 15000;
    private const string LegacyRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyRunValueName = "OptiSensor";

    private static readonly TimeSpan RestartInterval = TimeSpan.FromMinutes(1);

    public static string StartupCommand => $"\"{AppPaths.CurrentExecutablePath}\" {StartupArgument}";

    public static StartupRegistrationResult Register()
    {
        SimpleLog.TryWrite("Task Scheduler registration started.");
        RemoveLegacyRunEntry();

        if (!File.Exists(AppPaths.CurrentExecutablePath))
        {
            var missingExecutableError = $"Current executable was not found: {AppPaths.CurrentExecutablePath}";
            SimpleLog.TryWrite($"Task Scheduler registration skipped: {missingExecutableError}");
            return StartupRegistrationResult.Failed(missingExecutableError);
        }

        if (!TryRegister(out var error))
        {
            SimpleLog.TryWrite($"Task Scheduler registration failed: {error}");
            return StartupRegistrationResult.Failed(error ?? "Unknown Task Scheduler registration error.");
        }

        SimpleLog.TryWrite("Task Scheduler registration completed.");
        return StartupRegistrationResult.Ok(registered: true);
    }

    public static StartupRegistrationResult Unregister()
    {
        SimpleLog.TryWrite("Task Scheduler unregister started.");
        RemoveLegacyRunEntry();

        var result = RunSchTasks($"/Delete /TN {Quote(TaskName)} /F");
        if (result.ExitCode == 0)
        {
            SimpleLog.TryWrite("Task Scheduler unregister completed.");
            return StartupRegistrationResult.Ok(registered: false);
        }

        if (!IsRegistered())
        {
            SimpleLog.TryWrite("Task Scheduler task was already absent.");
            return StartupRegistrationResult.Ok(registered: false);
        }

        var error = $"exitCode={result.ExitCode}, stdout={result.Output}, stderr={result.Error}";
        SimpleLog.TryWrite($"Task Scheduler unregister failed: {error}");
        return StartupRegistrationResult.Failed(error);
    }

    public static bool IsRegistered()
    {
        var result = RunSchTasks($"/Query /TN {Quote(TaskName)}");
        return result.ExitCode == 0;
    }

    private static bool TryRegister(out string? error)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"OptiSensor-task-{Guid.NewGuid():N}.xml");
        try
        {
            WriteTaskXml(xmlPath);
            var result = RunSchTasks($"/Create /TN {Quote(TaskName)} /XML {Quote(xmlPath)} /F");
            SimpleLog.TryWrite($"Task Scheduler create result: exitCode={result.ExitCode}, stdout={result.Output}, stderr={result.Error}");
            if (result.ExitCode == 0)
            {
                error = null;
                return true;
            }

            error = $"exitCode={result.ExitCode}, stdout={result.Output}, stderr={result.Error}";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            TryDeleteFile(xmlPath);
        }
    }

    private static void WriteTaskXml(string path)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var currentUser = WindowsIdentity.GetCurrent().Name;
        var document = new XDocument(
            new XDeclaration("1.0", "utf-16", null),
            new XElement(ns + "Task",
                new XAttribute("version", "1.4"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", "OptiSensor tray helper startup and failure restart task.")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "LogonTrigger",
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "Delay", "PT5M"),
                        new XElement(ns + "UserId", currentUser))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", currentUser),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "AllowHardTerminate", "true"),
                    new XElement(ns + "StartWhenAvailable", "true"),
                    new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(ns + "IdleSettings",
                        new XElement(ns + "StopOnIdleEnd", "false"),
                        new XElement(ns + "RestartOnIdle", "false")),
                    new XElement(ns + "AllowStartOnDemand", "true"),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "Hidden", "false"),
                    new XElement(ns + "RunOnlyIfIdle", "false"),
                    new XElement(ns + "WakeToRun", "false"),
                    new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(ns + "Priority", "7"),
                    new XElement(ns + "RestartOnFailure",
                        new XElement(ns + "Interval", ToIsoDuration(RestartInterval)),
                        new XElement(ns + "Count", RestartCount.ToString(CultureInfo.InvariantCulture)))),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", AppPaths.CurrentExecutablePath),
                        new XElement(ns + "Arguments", StartupArgument),
                        new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(AppPaths.CurrentExecutablePath) ?? AppContext.BaseDirectory)))));

        using var writer = XmlWriter.Create(path, new XmlWriterSettings
        {
            Encoding = Encoding.Unicode,
            Indent = true
        });
        document.Save(writer);
    }

    private static (int ExitCode, string Output, string Error) RunSchTasks(string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(SchTasksTimeoutMs))
            {
                TryKill(process);
                return (-1, outputTask.GetAwaiter().GetResult(), "schtasks.exe timed out.");
            }

            return (process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return (-1, string.Empty, ex.Message);
        }
    }

    private static void RemoveLegacyRunEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKeyPath, writable: true);
            if (key?.GetValue(LegacyRunValueName) is null)
                return;

            key.DeleteValue(LegacyRunValueName, throwOnMissingValue: false);
            SimpleLog.TryWrite("Legacy HKCU Run entry removed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SimpleLog.TryWrite($"Could not remove legacy HKCU Run entry: {ex.Message}");
        }
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string ToIsoDuration(TimeSpan value)
    {
        return $"PT{Math.Max(1, (int)value.TotalMinutes).ToString(CultureInfo.InvariantCulture)}M";
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }
}
