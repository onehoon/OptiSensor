using System.Windows;
using OptiSensor.Install;
using OptiSensor.HWiNFO;
using OptiSensor.Models;
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
    private readonly CancellationTokenSource _startupCancellationTokenSource = new();

    private static readonly TimeSpan SharedMemoryRecoveryProbeWindow = TimeSpan.FromSeconds(5);
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
    public bool IsSensorSourceReady { get; private set; }

    public event EventHandler? SensorSourceReady;
    public event EventHandler<string>? SensorSourceStartupFailed;

    public static ApplicationHost Start(SingleInstanceGuard singleInstance, bool showMainWindow)
    {
        var settings = AppSettings.LoadOrCreate();
        EnsureStartupTaskForInstalledApp(settings);
        var publishService = new SensorPublishService(() => CreatePublishRunner(settings));
        var host = new ApplicationHost(singleInstance, settings, publishService);

        host.StartSensorServices();
        SimpleLog.TryWrite(showMainWindow ? "Application shell started." : "Startup mode shell started.");

        if (showMainWindow)
            host.ShowMainWindow();

        return host;
    }

    private static void EnsureStartupTaskForInstalledApp(AppSettings settings)
    {
        if (!settings.StartWithWindows || !AppPaths.IsRunningFromVelopackCurrentDirectory)
            return;

        var result = StartupRegistration.Register();
        if (!result.Success)
            SimpleLog.TryWrite($"Startup task refresh failed: {result.ErrorMessage}");
    }

    public static SensorPublishRunner CreatePublishRunner(AppSettings? settings = null)
    {
        settings ??= AppSettings.LoadOrCreate();

        ISensorReader sensorReader = settings.SensorSource == SensorSourceKind.HwInfo
            ? new HwInfoSensorReader()
            : new LibreSensorReader();
        return new SensorPublishRunner(
            sensorReader,
            new OverlayLineBuilder(),
            new ExternalOverlayPublisher(),
            settings.GetOverlayGroupsSnapshot);
    }

    public void ShowMainWindow()
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(ShowMainWindow);
            return;
        }

        if (!_mainWindow.IsVisible)
            _mainWindow.Show();

        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;

        _mainWindow.ShowInTaskbar = true;
        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();
    }

    public async void RequestExit()
    {
        if (IsExitRequested)
            return;

        IsExitRequested = true;
        SimpleLog.TryWrite("Application exit requested.");

        _startupCancellationTokenSource.Cancel();
        await _publishService.StopAsync();
        Dispose();
        System.Windows.Application.Current.Shutdown(0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _startupCancellationTokenSource.Cancel();
        _startupCancellationTokenSource.Dispose();
        _trayIcon.Dispose();
        _publishService.Dispose();
        _singleInstance.Dispose();
    }

    private void OnPublishServiceStatusChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _trayIcon.UpdateTooltip(_publishService.LastOverlayLine));
    }

    private void StartSensorServices()
    {
        if (_settings.SensorSource != SensorSourceKind.HwInfo)
        {
            StartPublishService();
            return;
        }

        SimpleLog.TryWrite("HWiNFO startup and shared-memory readiness monitoring started.");
        _ = StartHwInfoAndPublishWhenReadyAsync(_startupCancellationTokenSource.Token);
    }

    private async Task StartHwInfoAndPublishWhenReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startupResult = await HWiNFOStartupConfigurator
                .EnsureRunningAndWaitForSharedMemoryAsync(cancellationToken)
                .ConfigureAwait(false);
            SimpleLog.TryWrite(startupResult.Message);

            if (_disposed || cancellationToken.IsCancellationRequested)
                return;

            if (!startupResult.Ready)
            {
                SensorSourceStartupFailed?.Invoke(this, startupResult.Message);
                await ContinueWaitingForHwInfoSharedMemoryAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            StartPublishService();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SimpleLog.TryWrite("HWiNFO startup monitoring canceled.");
        }
        catch (Exception ex)
        {
            SimpleLog.TryWriteException(ex);
            if (!_disposed)
                SensorSourceStartupFailed?.Invoke(this, $"HWiNFO startup failed: {ex.Message}");
        }
    }

    private void StartPublishService()
    {
        if (_disposed)
            return;

        _publishService.Start(_settings.ClampedPublishIntervalMs);
        IsSensorSourceReady = true;
        SensorSourceReady?.Invoke(this, EventArgs.Empty);
    }

    private async Task ContinueWaitingForHwInfoSharedMemoryAsync(CancellationToken cancellationToken)
    {
        SimpleLog.TryWrite("HWiNFO shared memory recovery monitoring started.");

        while (!_disposed && !cancellationToken.IsCancellationRequested)
        {
            var recoveryResult = await HWiNFOStartupConfigurator
                .WaitForSharedMemoryAsync(SharedMemoryRecoveryProbeWindow, cancellationToken)
                .ConfigureAwait(false);

            if (!recoveryResult.Ready)
                continue;

            SimpleLog.TryWrite("HWiNFO shared memory recovered after startup timeout.");
            StartPublishService();
            return;
        }
    }
}
