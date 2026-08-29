namespace OptiSensor.App;

internal sealed record CommandLineOptions(bool Startup)
{
    public bool SuppressErrorDialog => Startup;

    public static CommandLineOptions Parse(string[] args)
    {
        return new CommandLineOptions(Startup: args.Contains("--startup", StringComparer.OrdinalIgnoreCase));
    }
}
