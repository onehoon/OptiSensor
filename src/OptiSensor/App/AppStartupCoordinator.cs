using System.Windows;

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
