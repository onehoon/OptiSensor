using OptiSensor.App;
using OptiSensor.Publishing;
using OptiSensor.Settings;

namespace OptiSensor.Cli;

internal static class CliCommands
{
    public static void RunOnce(Func<SensorPublishRunner> createRunner)
    {
        using var runner = createRunner();
        runner.Open();

        PrintResult(runner.PublishOnce());
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
        using var runner = createRunner();
        runner.Open();

        runner.RunLoopAsync(settings.ClampedPublishIntervalMs, PrintResult, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static void PrintResult(SensorPublishResult result)
    {
        TryClearConsole();
        Console.WriteLine(result.OverlayLine ?? "No GPU sensor values available.");
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
