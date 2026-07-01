using System.Windows;

namespace OptiSensor.App;

public partial class App : System.Windows.Application
{
    private ApplicationHost? _host;

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

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

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        SimpleLog.TryWrite("DispatcherUnhandledException captured.");
        SimpleLog.TryWriteException(e.Exception);
    }

    private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        SimpleLog.TryWrite($"CurrentDomain.UnhandledException captured. IsTerminating={e.IsTerminating}");
        if (e.ExceptionObject is Exception exception)
            SimpleLog.TryWriteException(exception);
        else if (e.ExceptionObject is not null)
            SimpleLog.TryWrite(e.ExceptionObject.ToString() ?? "Unhandled exception object is null.");
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        SimpleLog.TryWrite("TaskScheduler.UnobservedTaskException captured.");
        SimpleLog.TryWriteException(e.Exception);
    }
}
