using OptiSensor.App;
using OptiSensor.Publishing;

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

        using var runner = createRunner();
        runner.Open();

        runner.RunLoopAsync(1000, PrintResult, CancellationToken.None)
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
