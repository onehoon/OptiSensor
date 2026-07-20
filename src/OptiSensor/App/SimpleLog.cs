using OptiSensor.Install;

namespace OptiSensor.App;

internal static class SimpleLog
{
    private static readonly object Sync = new();

    public static void StartNewSession()
    {
        try
        {
            lock (Sync)
            {
                AppPaths.EnsureDataDirectories();
                File.WriteAllText(AppPaths.LogFilePath, string.Empty);
            }
        }
        catch
        {
        }
    }

    public static void TryWrite(string message)
    {
        try
        {
            lock (Sync)
            {
                AppPaths.EnsureDataDirectories();
                var now = DateTimeOffset.Now;
                File.AppendAllText(AppPaths.LogFilePath, $"[{now:O}] {message}{Environment.NewLine}");
            }
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
