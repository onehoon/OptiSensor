using OptiSensor.Install;

namespace OptiSensor.App;

internal static class SimpleLog
{
    public static void TryWrite(string message)
    {
        try
        {
            AppPaths.EnsureDataDirectories();
            File.AppendAllText(AppPaths.LogFilePath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    public static void TryWriteException(Exception exception)
    {
        TryWrite(exception.ToString());
    }
}
