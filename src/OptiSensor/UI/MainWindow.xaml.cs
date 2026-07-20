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
    private readonly SensorsPage _sensorsPage;
    private readonly OverlayPage _overlayPage;
    private readonly SettingsPage _settingsPage;
    private bool _hwInfoSharedMemoryWarningShown;
    private int _hwInfoSharedMemoryFailureCount;
    private bool _sensorRefreshStarted;
    private readonly DispatcherTimer _sensorRefreshTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(1000)
    };

    internal MainWindow(ApplicationHost host, SensorPublishService publishService, AppSettings settings)
    {
        InitializeComponent();
        Title = $"OptiSensor v{GetApplicationVersion()}";

        _host = host;
        _publishService = publishService;
        _settings = settings;
        _viewModel = new MainWindowViewModel(_settings);

        _sensorsPage = new SensorsPage { DataContext = _viewModel };
        _overlayPage = new OverlayPage { DataContext = _viewModel };
        _settingsPage = new SettingsPage { DataContext = _viewModel };
        _settingsPage.LoadSettings(_settings);
        _settingsPage.UpdateGitHubTokenState(GitHubTokenStore.HasToken());

        WirePageEvents();

        _publishService.StatusChanged += PublishService_StatusChanged;
        _host.SensorSourceReady += Host_SensorSourceReady;
        _host.SensorSourceStartupFailed += Host_SensorSourceStartupFailed;
        _viewModel.PropertyChanged += (_, _) => UpdateStatus();
        _sensorRefreshTimer.Tick += async (_, _) => await RefreshDetectedSensorsAsync();

        Loaded += (_, _) => StartSensorRefreshWhenReady();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                StartSensorRefreshWhenReady();
            else
                StopSensorRefresh();
        };
        NavigateTo(_overlayPage, OverlayNavButton);
        UpdateStatus();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_host.IsExitRequested)
        {
            e.Cancel = true;
            StopSensorRefresh();
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Minimized)
        {
            Hide();
            WindowState = WindowState.Normal;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _sensorRefreshTimer.Stop();
        _host.SensorSourceReady -= Host_SensorSourceReady;
        _host.SensorSourceStartupFailed -= Host_SensorSourceStartupFailed;
        _viewModel.Dispose();
        base.OnClosed(e);
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

        _settingsPage.SaveRequested += (_, _) => SaveSettings();
        _settingsPage.OpenSettingsFolderRequested += (_, _) => OpenSettingsFolder();
        _settingsPage.HideRequested += (_, _) => Hide();
        _settingsPage.ExitRequested += (_, _) => _host.RequestExit();
        _settingsPage.SaveGitHubTokenRequested += (_, token) => SaveGitHubToken(token);
        _settingsPage.RemoveGitHubTokenRequested += (_, _) => RemoveGitHubToken();
        _settingsPage.CheckForUpdatesRequested += async (_, _) => await CheckForUpdatesAsync();
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

    private void PublishService_StatusChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(UpdateStatus);
    }

    private void Host_SensorSourceReady(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(StartSensorRefreshWhenReady);
    }

    private void Host_SensorSourceStartupFailed(object? sender, string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_hwInfoSharedMemoryWarningShown || _host.IsExitRequested)
                return;

            _hwInfoSharedMemoryWarningShown = true;
            System.Windows.MessageBox.Show(
                $"HWiNFO started, but Shared Memory did not become available.\n\n{message}\n\nCheck that Shared Memory Support is enabled in HWiNFO Sensors settings.",
                "HWiNFO Shared Memory",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        });
    }

    private async void StartSensorRefreshWhenReady()
    {
        if (_sensorRefreshStarted ||
            !_host.IsSensorSourceReady ||
            !IsVisible)
            return;

        _sensorRefreshStarted = true;
        await RefreshDetectedSensorsAsync();

        if (IsVisible && ReferenceEquals(PageContentControl.Content, _sensorsPage))
            _sensorRefreshTimer.Start();
    }

    private void StopSensorRefresh()
    {
        _sensorRefreshTimer.Stop();
        _sensorRefreshStarted = false;
    }

    private void UpdateStatus()
    {
        var status = GetStatusText();
        var lastOverlay = _viewModel.GetOverlayPreviewText();
        var publishDetail =
            $"Interval {_settings.ClampedPublishIntervalMs} ms · Detected {_viewModel.DetectedSensorCount} · Selected {_viewModel.EnabledSelectedSensorCount}/{_viewModel.TotalSelectedSensorCount}";
        var settingsState = $"Settings: {_viewModel.SettingsStateText}";
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

    private async Task RefreshDetectedSensorsAsync()
    {
        if (_viewModel.IsRefreshing)
            return;

        if (ReferenceEquals(PageContentControl.Content, _sensorsPage) && _sensorsPage.ShouldDeferRefresh)
            return;

        try
        {
            _sensorsPage.CaptureScrollPosition();
            await _viewModel.RefreshDetectedSensorsAsync();
            _hwInfoSharedMemoryFailureCount = 0;
            _sensorsPage.RestoreScrollPosition();
            UpdateStatus();
        }
        catch (Exception ex)
        {
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

    private void SaveSettings()
    {
        _overlayPage.CommitEdits();
        var previousSensorSource = _settings.SensorSource;

        if (!_settingsPage.ApplySettingsEdits(_settings, out var settingsErrorMessage))
        {
            System.Windows.MessageBox.Show(settingsErrorMessage, "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedSensorSource = _settings.SensorSource;
        var sensorSourceChanged = selectedSensorSource != previousSensorSource;
        if (sensorSourceChanged)
            _settings.SensorSource = previousSensorSource;

        if (!_viewModel.TrySave(out var errorMessage))
        {
            _settings.SensorSource = previousSensorSource;
            System.Windows.MessageBox.Show(errorMessage, "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (sensorSourceChanged)
        {
            _settings.SensorSource = selectedSensorSource;
            _settings.Save();
        }

        var startupResult = _settings.StartWithWindows
            ? StartupRegistration.Register()
            : StartupRegistration.Unregister();

        _settingsPage.LoadSettings(_settings);
        UpdateStatus();

        if (!startupResult.Success)
        {
            System.Windows.MessageBox.Show(
                $"Settings saved, but startup registration failed.\n\n{startupResult.ErrorMessage}",
                "OptiSensor",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (sensorSourceChanged)
        {
            System.Windows.MessageBox.Show(
                "Sensor source was saved. OptiSensor will now close; start it again to apply the new source.",
                "OptiSensor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _host.RequestExit();
        }
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

    private void SaveGitHubToken(string token)
    {
        if (!GitHubTokenStore.Save(token, out var errorMessage))
        {
            _settingsPage.UpdateGitHubTokenState(GitHubTokenStore.HasToken(), errorMessage);
            return;
        }

        _settingsPage.UpdateGitHubTokenState(true, "Token saved in Windows Credential Manager. The update feed is not configured yet.");
    }

    private void RemoveGitHubToken()
    {
        if (!GitHubTokenStore.Delete(out var errorMessage))
        {
            _settingsPage.UpdateGitHubTokenState(GitHubTokenStore.HasToken(), errorMessage);
            return;
        }

        _settingsPage.UpdateGitHubTokenState(false, "Token removed from Windows Credential Manager.");
    }

    private async Task CheckForUpdatesAsync()
    {
        _settingsPage.SetUpdateCheckInProgress(true);
        try
        {
            var result = await GitHubUpdateService.DownloadLatestAsync(message =>
                Dispatcher.BeginInvoke(() => _settingsPage.UpdateGitHubTokenState(GitHubTokenStore.HasToken(), message)));

            _settingsPage.UpdateGitHubTokenState(GitHubTokenStore.HasToken(), result.Message);
            if (!result.IsReady)
                return;

            _settingsPage.UpdateGitHubTokenState(GitHubTokenStore.HasToken(), result.Message);
            GitHubUpdateService.ApplyAndRestart(result);
        }
        catch (Exception ex)
        {
            SimpleLog.TryWrite($"GitHub update check failed: {ex.Message}");
            _settingsPage.UpdateGitHubTokenState(
                GitHubTokenStore.HasToken(),
                "Could not check GitHub Releases. Verify the token has read access to this repository.");
        }
        finally
        {
            _settingsPage.SetUpdateCheckInProgress(false);
        }
    }

    private void SensorsNavButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_sensorsPage, SensorsNavButton);
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
        NavigateTo(_settingsPage, SettingsNavButton);
        StopSensorRefresh();
    }
}
