using System.Windows;
using OptiSensor.Install;
using OptiSensor.Libre;
using OptiSensor.Overlay;
using OptiSensor.Publishing;
using OptiSensor.Settings;
using OptiSensor.UI;

namespace OptiSensor.App;

internal sealed class ApplicationHost : IDisposable
{
    private readonly SingleInstanceGuard _singleInstance;
    private readonly AppSettings _settings;
    private readonly SensorPublishService _publishService;
    private readonly TrayIconService _trayIcon;
    private readonly MainWindow _mainWindow;

    private bool _disposed;

    private ApplicationHost(SingleInstanceGuard singleInstance, AppSettings settings, SensorPublishService publishService)
    {
        _singleInstance = singleInstance;
        _settings = settings;
        _publishService = publishService;
        _publishService.StatusChanged += OnPublishServiceStatusChanged;

        _trayIcon = new TrayIconService(ShowMainWindow, RequestExit);
        _mainWindow = new MainWindow(this, _publishService, _settings);
    }

    public bool IsExitRequested { get; private set; }

    public static ApplicationHost Start(SingleInstanceGuard singleInstance, bool showMainWindow)
    {
        var settings = AppSettings.LoadOrCreate();
        var publishService = new SensorPublishService(CreatePublishRunner(settings));
        var host = new ApplicationHost(singleInstance, settings, publishService);

        publishService.Start(settings.ClampedPublishIntervalMs);
        SimpleLog.TryWrite(showMainWindow ? "Application shell started." : "Startup mode shell started.");

        if (showMainWindow)
            host.ShowMainWindow();

        return host;
    }

    public static SensorPublishRunner CreatePublishRunner(AppSettings? settings = null)
    {
        settings ??= AppSettings.LoadOrCreate();

        return new SensorPublishRunner(
            new LibreSensorReader(),
            new OverlayLineBuilder(),
            new ExternalOverlayPublisher(),
            () => settings.EnabledSelectedSensors);
    }

    public void ShowMainWindow()
    {
        if (!_mainWindow.IsVisible)
            _mainWindow.Show();

        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;

        _mainWindow.Activate();
    }

    public async void RequestExit()
    {
        if (IsExitRequested)
            return;

        IsExitRequested = true;
        SimpleLog.TryWrite("Application exit requested.");

        await _publishService.StopAsync();
        Dispose();
        System.Windows.Application.Current.Shutdown(0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _trayIcon.Dispose();
        _publishService.Dispose();
        _singleInstance.Dispose();
    }

    private void OnPublishServiceStatusChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _trayIcon.UpdateTooltip(_publishService.LastOverlayLine));
    }
}
