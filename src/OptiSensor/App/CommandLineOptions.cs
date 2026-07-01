namespace OptiSensor.App;

internal sealed record CommandLineOptions(
    bool Install,
    bool Uninstall,
    bool Once,
    bool Watch,
    bool Startup)
{
    public bool IsCliMode => Install || Uninstall || Once || Watch;

    public static CommandLineOptions Parse(string[] args)
    {
        return new CommandLineOptions(
            Install: HasArg(args, "--install"),
            Uninstall: HasArg(args, "--uninstall"),
            Once: HasArg(args, "--once"),
            Watch: HasArg(args, "--watch"),
            Startup: HasArg(args, "--startup"));
    }

    private static bool HasArg(string[] args, string option)
    {
        return args.Contains(option, StringComparer.OrdinalIgnoreCase);
    }
}
