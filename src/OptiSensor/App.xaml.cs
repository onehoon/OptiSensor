using System.Windows;

namespace OptiSensor;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _singleInstance;
    private SensorPublishService? _publishService;
    private TrayIconService? _trayIcon;
    private MainWindow? _mainWindow;
    private AppSettings? _settings;

    internal bool IsExitRequested { get; private set; }

    internal static App CurrentApp => (App)System.Windows.Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            HandleStartup(e.Args);
        }
        catch (Exception ex)
        {
            SimpleLog.TryWriteException(ex);
            if (!HasArg(e.Args, "--startup"))
                System.Windows.MessageBox.Show(ex.Message, "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DisposeShell();
        base.OnExit(e);
    }

    private void HandleStartup(string[] args)
    {
        if (HasArg(args, "--install"))
        {
            ConsoleBridge.AttachForCliMode();
            AppInstaller.Install(verbose: true);
            Shutdown(0);
            return;
        }

        if (HasArg(args, "--uninstall"))
        {
            ConsoleBridge.AttachForCliMode();
            AppInstaller.Uninstall();
            Shutdown(0);
            return;
        }

        if (HasArg(args, "--once"))
        {
            ConsoleBridge.AttachForCliMode();
            CliCommands.RunOnce();
            Shutdown(0);
            return;
        }

        if (HasArg(args, "--watch"))
        {
            ConsoleBridge.AttachForCliMode();
            CliCommands.RunWatch();
            Shutdown(0);
            return;
        }

        var startup = HasArg(args, "--startup");
        if (AppInstaller.EnsureInstalledAndRelaunchIfNeeded(startup))
        {
            Shutdown(0);
            return;
        }

        _singleInstance = SingleInstanceGuard.TryAcquire();
        if (_singleInstance is null)
        {
            SimpleLog.TryWrite("OptiSensor is already running.");
            if (!startup)
                System.Windows.MessageBox.Show("OptiSensor is already running.", "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Information);

            Shutdown(0);
            return;
        }

        StartApplicationShell(showMainWindow: !startup);
    }

    private void StartApplicationShell(bool showMainWindow)
    {
        _settings = AppSettings.LoadOrCreate();

        _publishService = new SensorPublishService();
        _publishService.StatusChanged += OnPublishServiceStatusChanged;
        _publishService.Start(_settings.ClampedPublishIntervalMs);

        _trayIcon = new TrayIconService(ShowMainWindow, RequestExit);
        _mainWindow = new MainWindow(_publishService, _settings);

        SimpleLog.TryWrite(showMainWindow ? "Application shell started." : "Startup mode shell started.");

        if (showMainWindow)
            ShowMainWindow();
    }

    internal void ShowMainWindow()
    {
        if (_mainWindow is null)
            return;

        if (!_mainWindow.IsVisible)
            _mainWindow.Show();

        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;

        _mainWindow.Activate();
    }

    internal async void RequestExit()
    {
        if (IsExitRequested)
            return;

        IsExitRequested = true;
        SimpleLog.TryWrite("Application exit requested.");

        if (_publishService is not null)
            await _publishService.StopAsync();

        DisposeShell();
        Shutdown(0);
    }

    private void OnPublishServiceStatusChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => _trayIcon?.UpdateTooltip(_publishService?.LastOverlayLine));
    }

    private void DisposeShell()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;

        _publishService?.Dispose();
        _publishService = null;

        _singleInstance?.Dispose();
        _singleInstance = null;
    }

    private static bool HasArg(string[] args, string option)
    {
        return args.Contains(option, StringComparer.OrdinalIgnoreCase);
    }
}
