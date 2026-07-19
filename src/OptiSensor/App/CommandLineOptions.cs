namespace OptiSensor.App;

internal sealed record CommandLineOptions(
    bool Once,
    bool Watch,
    bool Startup,
    bool ConfigureHwInfoSharedMemory)
{
    public bool IsCliMode => Once || Watch || ConfigureHwInfoSharedMemory;
    public bool SuppressErrorDialog => Startup || ConfigureHwInfoSharedMemory;

    public static CommandLineOptions Parse(string[] args)
    {
        return new CommandLineOptions(
            Once: HasArg(args, "--once"),
            Watch: HasArg(args, "--watch"),
            Startup: HasArg(args, "--startup"),
            ConfigureHwInfoSharedMemory: HasArg(args, "--configure-hwinfo-shared-memory"));
    }

    private static bool HasArg(string[] args, string option)
    {
        return args.Contains(option, StringComparer.OrdinalIgnoreCase);
    }
}
