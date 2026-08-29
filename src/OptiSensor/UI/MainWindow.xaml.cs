using System.Diagnostics;
using System.Reflection;
using System.Windows;
using OptiSensor.App;
using OptiSensor.Install;
using OptiSensor.Publishing;
using OptiSensor.Settings;
using OptiSensor.UI.Views.Pages;
using OptiSensor.Updates;
using System.Windows.Threading;

namespace OptiSensor.UI;

public partial class MainWindow : Window
{
    private readonly ApplicationHost _host;
    private readonly SensorPublishService _publishService;
    private readonly AppSettings _settings;
    private readonly MainWindowViewModel _viewModel;
    private SensorsPage? _sensorsPage;
    private readonly OverlayPage _overlayPage;
    private SettingsPage? _settingsPage;
    private bool _hwInfoSharedMemoryWarningShown;
    private int _hwInfoSharedMemoryFailureCount;
    private bool _sensorRefreshStarted;
    private DispatcherTimer? _sensorRefreshTimer;
    private readonly CancellationTokenSource _windowLifetimeCancellation = new();
    private Task _activeSensorRefreshTask = Task.CompletedTask;
    private Task _activeUpdateCheckTask = Task.CompletedTask;
    private bool _sensorRefreshResumeRequested;
    private Task? _prepareShutdownTask;
    private bool _isShuttingDown;
    private bool _viewModelDisposed;
    private bool _allowPermanentClose;

    internal MainWindow(ApplicationHost host, SensorPublishService publishService, AppSettings settings)
    {
        InitializeComponent();
        Title = $"OptiSensor v{GetApplicationVersion()}";

        _host = host;
        _publishService = publishService;
        _settings = settings;
        _viewModel = new MainWindowViewModel(_settings);

        _overlayPage = new OverlayPage { DataContext = _viewModel };

        WirePageEvents();

        _publishService.StatusChanged += PublishService_StatusChanged;
        _host.SensorSourceReady += Host_SensorSourceReady;
        _host.SensorSourceStartupFailed += Host_SensorSourceStartupFailed;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        IsVisibleChanged += MainWindow_IsVisibleChanged;
        NavigateTo(_overlayPage, OverlayNavButton);
        UpdateStatus();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_host.IsExitRequested && !_allowPermanentClose)
        {
            e.Cancel = true;
            _host.RequestHideMainWindow();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Minimized)
        {
            _host.RequestHideMainWindow();
            WindowState = WindowState.Normal;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        DetachLifetimeEventHandlers();
        _sensorRefreshTimer?.Stop();

        // Normal application shutdown and clean UI-session teardown both run the lifetime
        // cleanup pipeline before permanently closing this window. This fallback only covers
        // an abnormal close that bypassed that pipeline.
        if (!_viewModelDisposed && _activeSensorRefreshTask.IsCompleted)
        {
            try
            {
                _viewModel.Dispose();
                _viewModelDisposed = true;
            }
            catch (Exception ex)
            {
                SimpleLog.TryWrite($"Final MainWindowViewModel disposal failed: {ex.Message}");
                SimpleLog.TryWriteException(ex);
            }
        }

        base.OnClosed(e);
    }

    /// <summary>
    /// Completes this window's UI lifetime before full application shutdown. Shares the same
    /// idempotent cleanup task used by clean UI-session teardown.
    /// </summary>
    internal Task PrepareForShutdownAsync() =>
        _prepareShutdownTask ??= RunPrepareForLifetimeEndAsync();

    /// <summary>
    /// Completes this window's UI lifetime before the host permanently retires a clean
    /// UI session while the background runtime remains alive.
    /// </summary>
    internal Task PrepareForSessionTeardownAsync() =>
        _prepareShutdownTask ??= RunPrepareForLifetimeEndAsync();

    private async Task RunPrepareForLifetimeEndAsync()
    {
        _isShuttingDown = true;

        _sensorRefreshTimer?.Stop();
        _sensorRefreshStarted = false;
        _windowLifetimeCancellation.Cancel();

        DetachLifetimeEventHandlers();

        try
        {
            await _activeSensorRefreshTask.ConfigureAwait(false);
            SimpleLog.TryWrite("Active sensor refresh completed.");
        }
        catch (OperationCanceledException)
        {
            SimpleLog.TryWrite("Active sensor refresh canceled.");
        }
        catch (Exception ex)
        {
            SimpleLog.TryWrite($"Active sensor refresh ended with error during shutdown: {ex.Message}");
        }

        try
        {
            await _activeUpdateCheckTask.ConfigureAwait(false);
            SimpleLog.TryWrite("Active UI update check completed.");
        }
        catch (OperationCanceledException)
        {
            SimpleLog.TryWrite("Active UI update check canceled.");
        }
        catch (Exception ex)
        {
            SimpleLog.TryWrite($"Active UI update check ended with error during teardown: {ex.Message}");
            SimpleLog.TryWriteException(ex);
        }

        try
        {
            if (!_viewModelDisposed)
            {
                _viewModel.Dispose();
                _viewModelDisposed = true;
            }
        }
        catch (Exception ex)
        {
            SimpleLog.TryWrite($"MainWindowViewModel disposal failed: {ex.Message}");
            SimpleLog.TryWriteException(ex);
        }
        finally
        {
            _activeUpdateCheckTask = Task.CompletedTask;
            _windowLifetimeCancellation.Dispose();
        }
    }

    private void DetachLifetimeEventHandlers()
    {
        _publishService.StatusChanged -= PublishService_StatusChanged;
        _host.SensorSourceReady -= Host_SensorSourceReady;
        _host.SensorSourceStartupFailed -= Host_SensorSourceStartupFailed;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        if (_settingsPage is not null)
        {
            _settingsPage.EditsChanged -= SettingsPage_EditsChanged;
            _settingsPage.CheckForUpdatesRequested -= SettingsPage_CheckForUpdatesRequested;
        }
        if (_sensorRefreshTimer is not null)
            _sensorRefreshTimer.Tick -= SensorRefreshTimer_Tick;
        IsVisibleChanged -= MainWindow_IsVisibleChanged;
    }

    private void WirePageEvents()
    {
        _overlayPage.MoveUpRequested += (_, _) => MoveSelectedSensorUp();
        _overlayPage.MoveDownRequested += (_, _) => MoveSelectedSensorDown();
        _overlayPage.RemoveRequested += (_, _) => RemoveSelectedSensor();
        _overlayPage.AddGroupRequested += (_, _) => AddOverlayGroup();
        _overlayPage.MoveGroupUpRequested += (_, _) => MoveOverlayGroupUp();
        _overlayPage.MoveGroupDownRequested += (_, _) => MoveOverlayGroupDown();
        _overlayPage.RemoveGroupRequested += (_, _) => RemoveOverlayGroup();
        _overlayPage.SaveRequested += (_, _) => SaveSettings();

    }

    private SensorsPage GetOrCreateSensorsPage() =>
        _sensorsPage ??= new SensorsPage { DataContext = _viewModel };

    private SettingsPage GetOrCreateSettingsPage()
    {
        if (_settingsPage is not null)
            return _settingsPage;

        var page = new SettingsPage { DataContext = _viewModel };
        page.LoadSettings(_settings);
        page.SaveRequested += (_, _) => SaveSettings();
        page.OpenSettingsFolderRequested += (_, _) => OpenSettingsFolder();
        page.HideRequested += (_, _) => _host.RequestHideMainWindow();
        page.ExitRequested += (_, _) => _host.RequestExit();
        page.CheckForUpdatesRequested += SettingsPage_CheckForUpdatesRequested;
        page.EditsChanged += SettingsPage_EditsChanged;
        return _settingsPage = page;
    }

    internal bool HasUnsavedChanges => IsDirty();

    internal bool ShouldPreserveSessionOnHide => IsDirty();

    internal void HidePreservingSession()
    {
        StopSensorRefresh();
        Hide();
    }

    internal void HideForSessionTeardown()
    {
        StopSensorRefresh();
        Hide();
    }

    internal void CloseAfterSessionTeardown()
    {
        _allowPermanentClose = true;
        Close();
    }

    private bool IsSensorsPageActive =>
        _sensorsPage is not null && ReferenceEquals(PageContentControl.Content, _sensorsPage);

    private DispatcherTimer GetOrCreateSensorRefreshTimer()
    {
        if (_sensorRefreshTimer is not null)
            return _sensorRefreshTimer;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        timer.Tick += SensorRefreshTimer_Tick;
        return _sensorRefreshTimer = timer;
    }

    private static string GetApplicationVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(MainWindow).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion.Split('+')[0];

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private bool IsShutdownInProgress() =>
        _isShuttingDown || _host.IsExitRequested || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished;

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (IsShutdownInProgress())
            return;

        UpdateStatus();
    }

    private void SettingsPage_EditsChanged(object? sender, EventArgs e)
    {
        if (IsShutdownInProgress())
            return;

        UpdateStatus();
    }

    private void SensorRefreshTimer_Tick(object? sender, EventArgs e)
    {
        QueueSensorRefresh();
    }

    private void MainWindow_IsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            StartSensorRefreshWhenReady();
        else
            StopSensorRefresh();
    }

    private void PublishService_StatusChanged(object? sender, EventArgs e)
    {
        if (!IsVisible || IsShutdownInProgress())
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible && !IsShutdownInProgress())
                UpdateStatus();
        });
    }

    private void Host_SensorSourceReady(object? sender, EventArgs e)
    {
        if (IsShutdownInProgress())
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (!IsShutdownInProgress() && IsSensorsPageActive)
                StartSensorRefreshWhenReady();
        });
    }

    private void Host_SensorSourceStartupFailed(object? sender, string message)
    {
        if (IsShutdownInProgress())
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (_hwInfoSharedMemoryWarningShown || IsShutdownInProgress())
                return;

            _hwInfoSharedMemoryWarningShown = true;
            System.Windows.MessageBox.Show(
                $"HWiNFO started, but Shared Memory did not become available.\n\n{message}\n\nCheck that Shared Memory Support is enabled in HWiNFO Sensors settings.",
                "HWiNFO Shared Memory",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        });
    }

    private void StartSensorRefreshWhenReady()
    {
        if (_isShuttingDown ||
            _sensorRefreshStarted ||
            !_host.IsSensorSourceReady ||
            !IsVisible ||
            !IsSensorsPageActive)
            return;

        _sensorRefreshStarted = true;

        // Never overwrite an in-flight refresh Task: the shutdown pipeline awaits
        // _activeSensorRefreshTask before disposing the sensor reader, so replacing an
        // unfinished Task here (e.g. rapid Hide -> Show while discovery is running)
        // would let that discovery escape tracking and race the dispose. If one is
        // already running, only record that another pass is wanted once it finishes -
        // don't stack a second wrapper Task around it, so repeated Hide/Show can't
        // queue up an unbounded chain of refreshes.
        if (!_activeSensorRefreshTask.IsCompleted)
        {
            _sensorRefreshResumeRequested = true;
            return;
        }

        _activeSensorRefreshTask = RunSensorRefreshCycleAsync(_windowLifetimeCancellation.Token);
    }

    private async Task RunSensorRefreshCycleAsync(CancellationToken cancellationToken)
    {
        await RunSensorRefreshAsync(cancellationToken).ConfigureAwait(true);

        if (!_isShuttingDown && IsVisible && IsSensorsPageActive)
            GetOrCreateSensorRefreshTimer().Start();

        if (!_sensorRefreshResumeRequested)
            return;

        _sensorRefreshResumeRequested = false;

        // Re-check right before resuming: the window may have been hidden, navigated
        // away, or had refresh stopped again while this cycle was still finishing.
        if (_isShuttingDown || !_sensorRefreshStarted || !IsVisible)
            return;

        await RunSensorRefreshCycleAsync(cancellationToken).ConfigureAwait(true);
    }

    private void StopSensorRefresh()
    {
        _sensorRefreshTimer?.Stop();
        _sensorRefreshStarted = false;
        _sensorRefreshResumeRequested = false;
    }

    /// <summary>
    /// Starts at most one sensor refresh at a time; a tick while one is still running
    /// is simply skipped (not queued for resume) - the next 1-second tick will retry.
    /// </summary>
    private void QueueSensorRefresh()
    {
        if (_isShuttingDown || !_activeSensorRefreshTask.IsCompleted || !IsSensorsPageActive)
            return;

        _activeSensorRefreshTask = RunSensorRefreshCycleAsync(_windowLifetimeCancellation.Token);
    }

    private void UpdateStatus()
    {
        var status = GetStatusText();
        var runtimeSensors = _publishService.LastSensors;
        string lastOverlay;
        if (runtimeSensors is not null)
        {
            _viewModel.UpdateSelectedSensorRuntimeValues(runtimeSensors);
            lastOverlay = _viewModel.GetOverlayPreviewText(runtimeSensors);
        }
        else
        {
            lastOverlay = _viewModel.GetOverlayPreviewText();
        }
        var publishDetail =
            $"Interval {_settings.ClampedPublishIntervalMs} ms · Detected {_viewModel.DetectedSensorCount} · Selected {_viewModel.EnabledSelectedSensorCount}/{_viewModel.TotalSelectedSensorCount}";
        var settingsState = $"Settings: {(IsDirty() ? "Unsaved changes" : "Saved")}";
        var optiScalerStatus = _publishService.LastOverlayLine is null
            ? "Waiting for publishable sensor values"
            : "Shared memory feed active";

        if (_viewModel.IsRefreshing)
            status = "Refreshing sensors...";

        _overlayPage.UpdatePreview(lastOverlay);
    }

    private string GetStatusText()
    {
        if (_publishService.LastError is not null)
            return $"Error: {_publishService.LastError}";

        return _publishService.IsRunning ? "Running" : "Stopped";
    }

    private void NavigateTo(System.Windows.Controls.UserControl page, System.Windows.Controls.Button selectedButton)
    {
        PageContentControl.Content = page;
        ResetNavButton(SensorsNavButton);
        ResetNavButton(OverlayNavButton);
        ResetNavButton(SettingsNavButton);
        selectedButton.Tag = "Selected";
    }

    private static void ResetNavButton(System.Windows.Controls.Button button)
    {
        button.Tag = null;
    }

    private async Task RunSensorRefreshAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        if (_viewModel.IsRefreshing)
            return;

        if (!IsSensorsPageActive || _sensorsPage!.ShouldDeferRefresh)
            return;

        try
        {
            _sensorsPage!.CaptureScrollPosition();
            await _viewModel.RefreshDetectedSensorsAsync(cancellationToken).ConfigureAwait(true);
            _hwInfoSharedMemoryFailureCount = 0;
            _sensorsPage!.RestoreScrollPosition();

            if (!_isShuttingDown)
                UpdateStatus();
        }
        catch (OperationCanceledException)
        {
            SimpleLog.TryWrite("Sensor refresh canceled.");
        }
        catch (Exception ex)
        {
            if (_isShuttingDown)
            {
                SimpleLog.TryWrite($"RefreshDetectedSensorsAsync failed during shutdown: {ex.Message}");
                return;
            }

            SimpleLog.TryWrite($"RefreshDetectedSensorsAsync failed: {ex.Message}");
            SimpleLog.TryWriteException(ex);
            if (_settings.SensorSource == Models.SensorSourceKind.HwInfo)
                _hwInfoSharedMemoryFailureCount++;

            var isHwInfoSharedMemoryFailure = _settings.SensorSource == Models.SensorSourceKind.HwInfo &&
                _hwInfoSharedMemoryFailureCount >= 5 &&
                !_hwInfoSharedMemoryWarningShown;
            if (isHwInfoSharedMemoryFailure)
            {
                _hwInfoSharedMemoryWarningShown = true;
                System.Windows.MessageBox.Show(
                    "HWiNFO Shared Memory를 읽을 수 없습니다.\n\n" +
                    "HWiNFO가 실행 중인지, Sensors 설정의 Shared Memory Support가 켜져 있는지 확인하세요.\n" +
                    "설정을 변경했다면 HWiNFO를 다시 시작해야 합니다.",
                    "HWiNFO Shared Memory",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else if (_settings.SensorSource != Models.SensorSourceKind.HwInfo)
            {
                System.Windows.MessageBox.Show(ex.Message, "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void RemoveSelectedSensor()
    {
        if (_overlayPage.SelectedOverlaySensor is not { } selectedSensor)
            return;

        _viewModel.RemoveSelectedSensor(selectedSensor);
        UpdateStatus();
    }

    private void MoveSelectedSensorUp()
    {
        if (_overlayPage.SelectedOverlaySensor is not { } selectedSensor)
            return;

        _viewModel.MoveSelectedSensorUp(selectedSensor);
        _overlayPage.SelectOverlaySensor(selectedSensor);
        UpdateStatus();
    }

    private void MoveSelectedSensorDown()
    {
        if (_overlayPage.SelectedOverlaySensor is not { } selectedSensor)
            return;

        _viewModel.MoveSelectedSensorDown(selectedSensor);
        _overlayPage.SelectOverlaySensor(selectedSensor);
        UpdateStatus();
    }

    private void AddOverlayGroup()
    {
        _viewModel.AddOverlayGroup();
        UpdateStatus();
    }

    private void RemoveOverlayGroup()
    {
        if (_overlayPage.SelectedGroup is not { } group)
            return;

        _viewModel.RemoveOverlayGroup(group);
        UpdateStatus();
    }

    private void MoveOverlayGroupUp()
    {
        if (_overlayPage.SelectedGroup is not { } group)
            return;

        _viewModel.MoveOverlayGroupUp(group);
        _viewModel.SelectedOverlayGroup = group;
        UpdateStatus();
    }

    private void MoveOverlayGroupDown()
    {
        if (_overlayPage.SelectedGroup is not { } group)
            return;

        _viewModel.MoveOverlayGroupDown(group);
        _viewModel.SelectedOverlayGroup = group;
        UpdateStatus();
    }

    private sealed record SaveSettingsResult(bool Success, bool SensorSourceChanged)
    {
        public static readonly SaveSettingsResult Failed = new(false, false);
    }

    private bool IsDirty() => _viewModel.HasUnsavedChanges || (_settingsPage?.HasUnsavedChanges ?? false);

    /// <summary>
    /// Called by ApplicationHost.RequestExit() before it commits to shutting down.
    /// Must not call _host.RequestExit() itself (that would recurse back here).
    /// </summary>
    internal bool TryPrepareForExit()
    {
        if (!IsDirty())
            return true;

        var choice = System.Windows.MessageBox.Show(
            "You have unsaved settings changes.\n\nSave before exiting?",
            "OptiSensor",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return choice switch
        {
            MessageBoxResult.Yes => TrySaveSettings().Success,
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void SaveSettings()
    {
        var result = TrySaveSettings();
        if (!result.Success)
            return;

        if (result.SensorSourceChanged)
        {
            System.Windows.MessageBox.Show(
                "Sensor source was saved. OptiSensor will now close; start it again to apply the new source.",
                "OptiSensor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _host.RequestExit();
        }
    }

    private bool TryGetGeneralSettingsDraft(out GeneralSettingsDraft? draft, out string? errorMessage)
    {
        if (_settingsPage is not null)
            return _settingsPage.TryCreateDraft(out draft, out errorMessage);

        draft = new GeneralSettingsDraft(
            _settings.StartWithWindows,
            _settings.SensorSource,
            _settings.ClampedPublishIntervalMs);
        errorMessage = null;
        return true;
    }

    /// <summary>
    /// The single Save transaction shared by the Overlay and Settings pages' Save
    /// buttons, and by the dirty-exit confirmation. Validates the full Draft,
    /// builds a candidate copy of AppSettings, and only applies anything to the
    /// live AppSettings / disk / startup registration after every validation
    /// (including the candidate.Save() disk write) has succeeded. On any failure,
    /// live AppSettings, settings.json, publish interval, and startup registration
    /// are left exactly as they were, and the UI Draft is preserved.
    /// </summary>
    private SaveSettingsResult TrySaveSettings()
    {
        _overlayPage.CommitEdits();

        if (!TryGetGeneralSettingsDraft(out var generalDraft, out var settingsErrorMessage))
        {
            System.Windows.MessageBox.Show(settingsErrorMessage, "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return SaveSettingsResult.Failed;
        }

        var candidate = _settings.CreateCopy();

        if (!_viewModel.TryApplyDraftTo(candidate, out var overlayErrorMessage))
        {
            System.Windows.MessageBox.Show(overlayErrorMessage, "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return SaveSettingsResult.Failed;
        }

        var sensorSourceChanged = generalDraft!.SensorSource != _settings.SensorSource;

        candidate.StartWithWindows = generalDraft.StartWithWindows;
        candidate.PublishIntervalMs = generalDraft.PublishIntervalMs;
        candidate.SensorSource = generalDraft.SensorSource;

        try
        {
            candidate.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(
                $"Could not save settings.\n\n{ex.Message}",
                "OptiSensor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return SaveSettingsResult.Failed;
        }

        _settings.ApplyFrom(candidate, preserveCurrentSensorSource: sensorSourceChanged);
        _publishService.UpdatePublishInterval(_settings.ClampedPublishIntervalMs);

        var startupResult = _settings.StartWithWindows
            ? StartupRegistration.Register()
            : StartupRegistration.Unregister();

        _viewModel.MarkSaved();
        _settingsPage?.AcceptSavedDraft(generalDraft);
        UpdateStatus();

        if (!startupResult.Success)
        {
            System.Windows.MessageBox.Show(
                $"Settings were saved, but startup registration failed.\n\n{startupResult.ErrorMessage}",
                "OptiSensor",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        return new SaveSettingsResult(true, sensorSourceChanged);
    }

    private static void OpenSettingsFolder()
    {
        AppPaths.EnsureDataDirectories();
        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.DataDirectory,
            UseShellExecute = true
        });
    }

    private async void SettingsPage_CheckForUpdatesRequested(object? sender, EventArgs e)
    {
        if (_isShuttingDown || !_activeUpdateCheckTask.IsCompleted || sender is not SettingsPage settingsPage)
            return;

        var task = CheckForUpdatesAsync(settingsPage, _windowLifetimeCancellation.Token);
        _activeUpdateCheckTask = task;

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SimpleLog.TryWrite($"UI update check ended unexpectedly: {ex.Message}");
            SimpleLog.TryWriteException(ex);
        }
    }

    private async Task CheckForUpdatesAsync(SettingsPage settingsPage, CancellationToken cancellationToken)
    {
        if (_isShuttingDown || cancellationToken.IsCancellationRequested)
            return;

        settingsPage.SetUpdateCheckInProgress(true);
        try
        {
            var result = await GitHubUpdateService.DownloadLatestAsync(message =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                _ = Dispatcher.BeginInvoke(() =>
                {
                    if (cancellationToken.IsCancellationRequested ||
                        IsShutdownInProgress() ||
                        !ReferenceEquals(_settingsPage, settingsPage))
                    {
                        return;
                    }

                    settingsPage.SetUpdateStatus(message);
                });
            }, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (cancellationToken.IsCancellationRequested ||
                IsShutdownInProgress() ||
                !ReferenceEquals(_settingsPage, settingsPage))
            {
                return;
            }

            settingsPage.SetUpdateStatus(result.Message);
            if (!result.IsReady)
                return;

            if (cancellationToken.IsCancellationRequested || IsShutdownInProgress())
                return;

            GitHubUpdateService.ApplyAndRestart(result);
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested || IsShutdownInProgress())
            {
                SimpleLog.TryWrite("UI update check completed after UI-session cancellation.");
                return;
            }

            SimpleLog.TryWrite($"GitHub update check failed: {ex.Message}");
            SimpleLog.TryWriteException(ex);

            if (ReferenceEquals(_settingsPage, settingsPage))
            {
                settingsPage.SetUpdateStatus("Could not check GitHub Releases. Try again later.");
            }
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested &&
                !IsShutdownInProgress() &&
                ReferenceEquals(_settingsPage, settingsPage))
            {
                settingsPage.SetUpdateCheckInProgress(false);
            }
        }
    }

    private void SensorsNavButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(GetOrCreateSensorsPage(), SensorsNavButton);
        StopSensorRefresh();
        StartSensorRefreshWhenReady();
    }

    private void OverlayNavButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_overlayPage, OverlayNavButton);
        StopSensorRefresh();
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(GetOrCreateSettingsPage(), SettingsNavButton);
        StopSensorRefresh();
    }
}
