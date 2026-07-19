namespace OptiSensor.App;

internal sealed record CommandLineOptions(
    bool Once,
    bool Watch,
    bool Startup)
{
    public bool IsCliMode => Once || Watch;

    public static CommandLineOptions Parse(string[] args)
    {
        return new CommandLineOptions(
            Once: HasArg(args, "--once"),
            Watch: HasArg(args, "--watch"),
            Startup: HasArg(args, "--startup"));
    }

    private static bool HasArg(string[] args, string option)
    {
        return args.Contains(option, StringComparer.OrdinalIgnoreCase);
    }
}
