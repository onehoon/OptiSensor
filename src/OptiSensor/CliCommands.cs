namespace OptiSensor;

internal static class CliCommands
{
    public static void RunOnce()
    {
        RunSensorLoop(showConsoleUpdates: true, runOnce: true, publishIntervalMs: 1000);
    }

    public static void RunWatch()
    {
        using var guard = SingleInstanceGuard.TryAcquire();
        if (guard is null)
        {
            Console.WriteLine("OptiSensor is already running.");
            return;
        }

        var settings = AppSettings.LoadOrCreate();
        RunSensorLoop(showConsoleUpdates: true, runOnce: false, settings.ClampedPublishIntervalMs);
    }

    private static void RunSensorLoop(bool showConsoleUpdates, bool runOnce, int publishIntervalMs)
    {
        using var sensorReader = new SensorReader();
        using var publisher = new ExternalOverlayPublisher();

        sensorReader.Open();
        publisher.Open();

        var delay = TimeSpan.FromMilliseconds(Math.Clamp(publishIntervalMs, 100, 10000));

        while (true)
        {
            var overlayLine = sensorReader.ReadOverlayLine();

            if (overlayLine is not null)
                publisher.Publish(overlayLine);

            if (showConsoleUpdates || runOnce)
            {
                TryClearConsole();
                Console.WriteLine(overlayLine ?? "No GPU sensor values available.");
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
