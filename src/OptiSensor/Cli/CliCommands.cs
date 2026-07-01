using OptiSensor.App;
using OptiSensor.Publishing;
using OptiSensor.Settings;

namespace OptiSensor.Cli;

internal static class CliCommands
{
    public static void RunOnce(Func<SensorPublishRunner> createRunner)
    {
        RunSensorLoop(createRunner, showConsoleUpdates: true, runOnce: true, publishIntervalMs: 1000);
    }

    public static void RunWatch(Func<SensorPublishRunner> createRunner)
    {
        using var guard = SingleInstanceGuard.TryAcquire();
        if (guard is null)
        {
            Console.WriteLine("OptiSensor is already running.");
            return;
        }

        var settings = AppSettings.LoadOrCreate();
        RunSensorLoop(createRunner, showConsoleUpdates: true, runOnce: false, settings.ClampedPublishIntervalMs);
    }

    private static void RunSensorLoop(
        Func<SensorPublishRunner> createRunner,
        bool showConsoleUpdates,
        bool runOnce,
        int publishIntervalMs)
    {
        using var runner = createRunner();
        runner.Open();

        var delay = TimeSpan.FromMilliseconds(Math.Clamp(publishIntervalMs, 100, 10000));

        while (true)
        {
            var result = runner.PublishOnce();

            if (showConsoleUpdates || runOnce)
            {
                TryClearConsole();
                Console.WriteLine(result.OverlayLine ?? "No GPU sensor values available.");
            }

            if (runOnce)
                break;

            Thread.Sleep(delay);
        }
    }

    private static void TryClearConsole()
    {
        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
        }
    }
}
