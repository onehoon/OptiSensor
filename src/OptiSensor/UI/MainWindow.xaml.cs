using System.Diagnostics;
using System.Windows;
using OptiSensor.App;
using OptiSensor.Install;
using OptiSensor.Publishing;
using OptiSensor.Settings;
using OptiSensor.UI.Views.Pages;

namespace OptiSensor.UI;

public partial class MainWindow : Window
{
    private readonly ApplicationHost _host;
    private readonly SensorPublishService _publishService;
    private readonly AppSettings _settings;
    private readonly MainWindowViewModel _viewModel;
    private readonly DashboardPage _dashboardPage;
    private readonly SensorsPage _sensorsPage;
    private readonly OverlayPage _overlayPage;
    private readonly SettingsPage _settingsPage;

    internal MainWindow(ApplicationHost host, SensorPublishService publishService, AppSettings settings)
    {
        InitializeComponent();

        _host = host;
        _publishService = publishService;
        _settings = settings;
        _viewModel = new MainWindowViewModel(_settings);

        _dashboardPage = new DashboardPage { DataContext = _viewModel };
        _sensorsPage = new SensorsPage { DataContext = _viewModel };
        _overlayPage = new OverlayPage { DataContext = _viewModel };
        _settingsPage = new SettingsPage { DataContext = _viewModel };
        _settingsPage.LoadSettings(_settings);

        WirePageEvents();

        _publishService.StatusChanged += PublishService_StatusChanged;
        _viewModel.PropertyChanged += (_, _) => UpdateStatus();

        Loaded += async (_, _) => await RefreshDetectedSensorsAsync();
        NavigateTo(_dashboardPage, DashboardNavButton);
        UpdateStatus();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_host.IsExitRequested)
        {
            e.Cancel = true;
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
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void WirePageEvents()
    {
        _sensorsPage.RefreshRequested += async (_, _) => await RefreshDetectedSensorsAsync();
        _sensorsPage.AddRequested += (_, _) => AddSelectedSensor();

        _overlayPage.MoveUpRequested += (_, _) => MoveSelectedSensorUp();
        _overlayPage.MoveDownRequested += (_, _) => MoveSelectedSensorDown();
        _overlayPage.RemoveRequested += (_, _) => RemoveSelectedSensor();

        _settingsPage.SaveRequested += (_, _) => SaveSettings();
        _settingsPage.OpenSettingsFolderRequested += (_, _) => OpenSettingsFolder();
        _settingsPage.HideRequested += (_, _) => Hide();
        _settingsPage.ExitRequested += (_, _) => _host.RequestExit();
    }

    private void PublishService_StatusChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(UpdateStatus);
    }

    private void UpdateStatus()
    {
        var status = GetStatusText();
        var lastOverlay = _publishService.LastOverlayLine ?? "No overlay line is currently published.";
        var publishDetail =
            $"Interval {_settings.ClampedPublishIntervalMs} ms · Detected {_viewModel.DetectedSensorCount} · Selected {_viewModel.EnabledSelectedSensorCount}/{_viewModel.TotalSelectedSensorCount}";
        var settingsState = $"Settings: {_viewModel.SettingsStateText}";
        var optiScalerStatus = _publishService.LastOverlayLine is null
            ? "Waiting for publishable sensor values"
            : "Shared memory feed active";

        if (_viewModel.IsRefreshing)
            status = "Refreshing sensors...";

        _dashboardPage.UpdateStatus(status, lastOverlay, publishDetail, settingsState, optiScalerStatus);
        _overlayPage.UpdatePreview(lastOverlay);
        _settingsPage.UpdateRuntime(_settings, _viewModel);
        _sensorsPage.IsRefreshEnabled = !_viewModel.IsRefreshing;
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
        ResetNavButton(DashboardNavButton);
        ResetNavButton(SensorsNavButton);
        ResetNavButton(OverlayNavButton);
        ResetNavButton(SettingsNavButton);
        selectedButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(219, 234, 254));
        selectedButton.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 64, 175));
    }

    private static void ResetNavButton(System.Windows.Controls.Button button)
    {
        button.Background = System.Windows.Media.Brushes.Transparent;
        button.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85));
    }

    private async Task RefreshDetectedSensorsAsync()
    {
        try
        {
            await _viewModel.RefreshDetectedSensorsAsync();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddSelectedSensor()
    {
        if (_sensorsPage.SelectedDetectedSensor is not { } detectedSensor)
            return;

        if (!_viewModel.AddDetectedSensor(detectedSensor))
            System.Windows.MessageBox.Show("This sensor is already selected.", "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Information);

        UpdateStatus();
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

    private void SaveSettings()
    {
        _overlayPage.CommitEdits();

        if (!_settingsPage.ApplySettingsEdits(_settings, out var settingsErrorMessage))
        {
            System.Windows.MessageBox.Show(settingsErrorMessage, "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_viewModel.TrySave(out var errorMessage))
        {
            System.Windows.MessageBox.Show(errorMessage, "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
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
            return;
        }

        System.Windows.MessageBox.Show("Settings saved.", "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void DashboardNavButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_dashboardPage, DashboardNavButton);
    }

    private void SensorsNavButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_sensorsPage, SensorsNavButton);
    }

    private void OverlayNavButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_overlayPage, OverlayNavButton);
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_settingsPage, SettingsNavButton);
    }
}
