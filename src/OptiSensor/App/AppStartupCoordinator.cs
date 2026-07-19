using System.Windows;
using OptiSensor.Cli;

namespace OptiSensor.App;

internal sealed class AppStartupCoordinator
{
    private readonly System.Windows.Application _application;

    public AppStartupCoordinator(System.Windows.Application application)
    {
        _application = application;
    }

    public ApplicationHost? Start(string[] args)
    {
        var options = CommandLineOptions.Parse(args);

        if (options.Once)
        {
            ConsoleBridge.AttachForCliMode();
            CliCommands.RunOnce(() => ApplicationHost.CreatePublishRunner());
            _application.Shutdown(0);
            return null;
        }

        if (options.Watch)
        {
            ConsoleBridge.AttachForCliMode();
            CliCommands.RunWatch(() => ApplicationHost.CreatePublishRunner());
            _application.Shutdown(0);
            return null;
        }

        var singleInstance = SingleInstanceGuard.TryAcquire();
        if (singleInstance is null)
        {
            SimpleLog.TryWrite("OptiSensor is already running.");
            if (!options.Startup)
                System.Windows.MessageBox.Show("OptiSensor is already running.", "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Information);

            _application.Shutdown(0);
            return null;
        }

        return ApplicationHost.Start(singleInstance, showMainWindow: !options.Startup);
    }
}
