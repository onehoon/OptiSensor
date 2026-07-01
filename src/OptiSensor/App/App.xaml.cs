using System.Windows;

namespace OptiSensor.App;

public partial class App : System.Windows.Application
{
    private ApplicationHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var coordinator = new AppStartupCoordinator(this);
            _host = coordinator.Start(e.Args);
        }
        catch (Exception ex)
        {
            SimpleLog.TryWriteException(ex);

            var options = CommandLineOptions.Parse(e.Args);
            if (!options.Startup)
                System.Windows.MessageBox.Show(ex.Message, "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _host = null;
        base.OnExit(e);
    }
}
