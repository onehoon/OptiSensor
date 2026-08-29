using System.Windows;
using OptiSensor.Install;
using OptiSensor.HWiNFO;
using OptiSensor.Models;
using OptiSensor.Libre;
using OptiSensor.Overlay;
using OptiSensor.Publishing;
using OptiSensor.Settings;
using OptiSensor.UI;
using OptiSensor.Updates;

namespace OptiSensor.App;

internal sealed class ApplicationHost : IDisposable
{
    private readonly SingleInstanceGuard _singleInstance;
    private readonly AppSettings _settings;
    private readonly SensorPublishService _publishService;
    private readonly TrayIconService _trayIcon;
    private MainWindow? _mainWindow;
    private readonly CancellationTokenSource _startupCancellationTokenSource = new();
    private Task _sensorStartupTask = Task.CompletedTask;
    private Task? _shutdownTask;
    private Task _mainWindowTeardownTask = Task.CompletedTask;
    private bool _mainWindowTeardownInProgress;
    private bool _showMainWindowAfterTeardown;

    private static readonly TimeSpan SharedMemoryRecoveryProbeWindow = TimeSpan.FromSeconds(5);
    private bool _disposed;

    private ApplicationHost(SingleInstanceGuard singleInstance, AppSettings settings, SensorPublishService publishService)
    {
        _singleInstance = singleInstance;
        _settings = settings;
        _publishService = publishService;
        _publishService.StatusChanged += OnPublishServiceStatusChanged;

        _trayIcon = new TrayIconService(ShowMainWindow, RequestExit);
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

        _ = host.CheckForUpdatesInBackgroundAsync();

        return host;
    }

    /// <summary>
    /// Runs a silent update check regardless of whether the main window is shown, so launches
    /// from Windows startup (which start hidden to the tray) still pick up updates instead of
    /// relying solely on the "Check for Updates" button in Settings.
    /// </summary>
    private async Task CheckForUpdatesInBackgroundAsync()
    {
        try
        {
            var result = await GitHubUpdateService
                .DownloadLatestAsync(message => SimpleLog.TryWrite($"Background update check: {message}"))
                .ConfigureAwait(false);

            SimpleLog.TryWrite($"Background update check: {result.Message}");

            if (result.IsReady && !IsExitCleanupInProgress(_startupCancellationTokenSource.Token))
            {
                var restartArgs = await IsMainWindowVisibleAsync().ConfigureAwait(false)
                    ? null
                    : new[] { "--startup" };
                GitHubUpdateService.ApplyAndRestart(result, restartArgs);
            }
        }
        catch (Exception ex)
        {
            SimpleLog.TryWrite($"Background update check failed: {ex.Message}");
        }
    }

    /// <summary>Checked on the UI thread so a restart after applying an update can stay hidden to the tray
    /// when the main window wasn't visible, instead of always popping the window open.</summary>
    private Task<bool> IsMainWindowVisibleAsync()
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        return dispatcher.CheckAccess()
            ? Task.FromResult(_mainWindow?.IsVisible == true)
            : dispatcher.InvokeAsync(() => _mainWindow?.IsVisible == true).Task;
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
        var outputComposer = new OverlayOutputComposer(new OverlayLineBuilder());
        return new SensorPublishRunner(
            sensorReader,
            outputComposer,
            new ExternalOverlayPublisher(),
            settings.GetOverlayGroupsSnapshot);
    }

    public void ShowMainWindow()
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            if (IsUiCreationBlocked(dispatcher))
                return;

            _ = dispatcher.BeginInvoke(ShowMainWindow);
            return;
        }

        // Shutdown may have started after this request was queued but before the
        // callback reached the dispatcher. Never create UI during host shutdown.
        if (IsUiCreationBlocked(dispatcher))
            return;

        if (_mainWindowTeardownInProgress)
        {
            _showMainWindowAfterTeardown = true;
            return;
        }

        if (_mainWindow is null)
        {
            _mainWindow = CreateMainWindow();
            SimpleLog.TryWrite("MainWindow UI session created.");
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

    private MainWindow CreateMainWindow() =>
        new(this, _publishService, _settings);

    private bool IsUiCreationBlocked(System.Windows.Threading.Dispatcher dispatcher)
    {
        return _disposed ||
            IsExitRequested ||
            dispatcher.HasShutdownStarted ||
            dispatcher.HasShutdownFinished;
    }

    internal void RequestHideMainWindow()
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(RequestHideMainWindow);
            return;
        }

        if (IsExitRequested || _shutdownTask is not null || _mainWindow is null)
            return;

        var window = _mainWindow;
        if (window.ShouldPreserveSessionOnHide)
        {
            window.HidePreservingSession();
            return;
        }

        if (_mainWindowTeardownInProgress)
            return;

        window.HideForSessionTeardown();
        _mainWindowTeardownInProgress = true;
        _mainWindowTeardownTask = TearDownMainWindowAsync(window);
    }

    public void RequestExit()
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(RequestExit);
            return;
        }

        if (IsExitRequested || _shutdownTask is not null)
            return;

        if (_mainWindow is not null && !_mainWindow.TryPrepareForExit())
            return;

        IsExitRequested = true;
        SimpleLog.TryWrite("Application exit requested.");

        _shutdownTask = ShutdownAsync();
    }

    private async Task ShutdownAsync()
    {
        SimpleLog.TryWrite("Application shutdown started.");

        _startupCancellationTokenSource.Cancel();

        var windowShutdownTask = WaitForMainWindowTeardownAsync();
        if (_mainWindow is not null)
            windowShutdownTask = Task.WhenAll(windowShutdownTask, _mainWindow.PrepareForShutdownAsync());
        var sensorStartupCompletion = ObserveSensorStartupCompletionAsync();

        try
        {
            await Task.WhenAll(windowShutdownTask, sensorStartupCompletion).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SimpleLog.TryWrite($"Error while completing shutdown cleanup: {ex.Message}");
        }

        // Unsubscribe before StopAsync: stopping raises StatusChanged, and a tooltip
        // callback queued by it could otherwise run after the tray icon is disposed.
        _publishService.StatusChanged -= OnPublishServiceStatusChanged;

        try
        {
            await _publishService.StopAsync().ConfigureAwait(false);
            SimpleLog.TryWrite("Sensor publish service stopped.");
        }
        catch (Exception ex)
        {
            SimpleLog.TryWrite($"Error while stopping sensor publish service: {ex.Message}");
        }

        // The awaits above may have hopped to the thread pool; Application.Shutdown
        // (and the tray icon disposal inside Dispose) must run on the WPF UI thread.
        try
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    Dispose();
                }
                catch (Exception ex)
                {
                    SimpleLog.TryWrite($"Error while disposing ApplicationHost: {ex.Message}");
                }

                SimpleLog.TryWrite("Application shutdown completed.");

                // The user asked to exit; a cleanup-step failure above doesn't change that,
                // so Task Scheduler should still see this as a normal exit, not a crash to restart.
                System.Windows.Application.Current.Shutdown(0);
            });
        }
        catch (Exception ex)
        {
            // Last-resort observation so a fault here can't strand the app half-shut-down
            // as an unobserved _shutdownTask failure.
            SimpleLog.TryWrite($"Error during final shutdown dispatch: {ex.Message}");
            SimpleLog.TryWriteException(ex);
        }
    }

    private async Task WaitForMainWindowTeardownAsync()
    {
        try
        {
            await _mainWindowTeardownTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SimpleLog.TryWrite($"UI session teardown failed during shutdown: {ex.Message}");
        }
    }

    private async Task TearDownMainWindowAsync(MainWindow window)
    {
        Exception? cleanupFailure = null;
        try
        {
            await window.PrepareForSessionTeardownAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            cleanupFailure = ex;
            SimpleLog.TryWrite($"UI session teardown preparation failed: {ex.Message}");
            SimpleLog.TryWriteException(ex);
        }

        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return;

        try
        {
            await dispatcher.InvokeAsync(() => CompleteMainWindowTeardown(window, cleanupFailure));
        }
        catch (Exception ex)
        {
            SimpleLog.TryWrite($"UI session teardown finalization failed: {ex.Message}");
            SimpleLog.TryWriteException(ex);

            if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
            {
                try
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        if (ReferenceEquals(_mainWindow, window))
                            _mainWindow = null;

                        _mainWindowTeardownInProgress = false;
                        _showMainWindowAfterTeardown = false;
                        MarkMainWindowTeardownFinished();
                    });
                }
                catch
                {
                    // Application shutdown may have won the race.
                }
            }
        }
    }

    private void CompleteMainWindowTeardown(MainWindow window, Exception? cleanupFailure)
    {
        try
        {
            window.CloseAfterSessionTeardown();
        }
        catch (Exception ex)
        {
            SimpleLog.TryWrite($"Failed to close retired MainWindow: {ex.Message}");
            SimpleLog.TryWriteException(ex);
        }

        if (ReferenceEquals(_mainWindow, window))
            _mainWindow = null;

        var reopen = _showMainWindowAfterTeardown;
        _showMainWindowAfterTeardown = false;
        MarkMainWindowTeardownFinished();

        if (cleanupFailure is not null)
            SimpleLog.TryWrite("Retired the failed UI session; background runtime remains active.");
        else
            SimpleLog.TryWrite("MainWindow UI session teardown completed.");

        if (reopen && !IsExitRequested && _shutdownTask is null && !_disposed)
            ShowMainWindow();
    }

    private void MarkMainWindowTeardownFinished()
    {
        // Do not keep the completed async teardown state machine rooted from the
        // process-lifetime host; it may retain the retired MainWindow graph.
        _mainWindowTeardownTask = Task.CompletedTask;
        _mainWindowTeardownInProgress = false;
    }

    private async Task ObserveSensorStartupCompletionAsync()
    {
        try
        {
            await _sensorStartupTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SimpleLog.TryWrite($"HWiNFO startup monitoring ended with error during shutdown: {ex.Message}");
        }
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
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (_disposed || IsExitRequested || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return;

        dispatcher.BeginInvoke(() =>
        {
            // Re-check inside the callback: shutdown may have started (and the tray
            // icon been disposed) between queueing and execution.
            if (_disposed || IsExitRequested || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;

            _trayIcon.UpdateTooltip(_publishService.LastOverlayLine);
        });
    }

    private void StartSensorServices()
    {
        if (_settings.SensorSource != SensorSourceKind.HwInfo)
        {
            StartPublishService();
            _sensorStartupTask = Task.CompletedTask;
            return;
        }

        SimpleLog.TryWrite("HWiNFO startup and shared-memory readiness monitoring started.");
        _sensorStartupTask = StartHwInfoAndPublishWhenReadyAsync(_startupCancellationTokenSource.Token);
    }

    private async Task StartHwInfoAndPublishWhenReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startupResult = await HWiNFOStartupConfigurator
                .EnsureRunningAndWaitForSharedMemoryAsync(cancellationToken)
                .ConfigureAwait(false);
            SimpleLog.TryWrite(startupResult.Message);

            if (IsExitCleanupInProgress(cancellationToken))
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
            if (!IsExitCleanupInProgress(cancellationToken))
                SensorSourceStartupFailed?.Invoke(this, $"HWiNFO startup failed: {ex.Message}");
        }
    }

    private void StartPublishService()
    {
        if (IsExitCleanupInProgress(_startupCancellationTokenSource.Token))
            return;

        _publishService.Start(_settings.ClampedPublishIntervalMs);
        IsSensorSourceReady = true;

        if (!IsExitCleanupInProgress(_startupCancellationTokenSource.Token))
            SensorSourceReady?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Guards against starting the publisher or raising ready/failed events once exit has begun.</summary>
    private bool IsExitCleanupInProgress(CancellationToken cancellationToken)
    {
        return _disposed || IsExitRequested || cancellationToken.IsCancellationRequested;
    }

    private async Task ContinueWaitingForHwInfoSharedMemoryAsync(CancellationToken cancellationToken)
    {
        SimpleLog.TryWrite("HWiNFO shared memory recovery monitoring started.");

        while (!IsExitCleanupInProgress(cancellationToken))
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
