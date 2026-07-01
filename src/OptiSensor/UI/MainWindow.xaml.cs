using System.Diagnostics;
using System.Windows;
using OptiSensor.App;
using OptiSensor.Install;
using OptiSensor.Publishing;
using OptiSensor.Settings;

namespace OptiSensor.UI;

public partial class MainWindow : Window
{
    private readonly ApplicationHost _host;
    private readonly SensorPublishService _publishService;
    private readonly AppSettings _settings;
    private readonly MainWindowViewModel _viewModel;

    internal MainWindow(ApplicationHost host, SensorPublishService publishService, AppSettings settings)
    {
        InitializeComponent();

        _host = host;
        _publishService = publishService;
        _settings = settings;
        _viewModel = new MainWindowViewModel(_settings);
        DataContext = _viewModel;

        _publishService.StatusChanged += PublishService_StatusChanged;
        _viewModel.PropertyChanged += (_, _) => UpdateStatus();

        Loaded += (_, _) => RefreshDetectedSensors();
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

    private void PublishService_StatusChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(UpdateStatus);
    }

    private void UpdateStatus()
    {
        if (_publishService.LastError is not null)
            StatusTextBlock.Text = $"Error: {_publishService.LastError}";
        else
            StatusTextBlock.Text = _publishService.IsRunning ? "Running" : "Stopped";

        LastOverlayTextBlock.Text = _publishService.LastOverlayLine ?? "No GPU sensor values available.";
        StartupTextBlock.Text = $"Start with Windows: {(_settings.StartWithWindows ? "Enabled" : "Disabled")}";
        PublishIntervalTextBlock.Text = $"Publish interval: {_settings.ClampedPublishIntervalMs} ms";
        DetectedSensorCountTextBlock.Text = $"Detected sensors: {_viewModel.DetectedSensorCount}";
        SelectedSensorCountTextBlock.Text = $"Selected sensors: {_publishService.SelectedSensorCount} / {_settings.SelectedSensors.Count}";
    }

    private void RefreshDetectedSensors_Click(object sender, RoutedEventArgs e)
    {
        RefreshDetectedSensors();
    }

    private void AddSelectedSensor_Click(object sender, RoutedEventArgs e)
    {
        if (DetectedSensorsDataGrid.SelectedItem is not DetectedSensorViewModel detectedSensor)
            return;

        if (!_viewModel.AddDetectedSensor(detectedSensor))
            System.Windows.MessageBox.Show("This sensor is already selected.", "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Information);

        UpdateStatus();
    }

    private void RemoveSelectedSensor_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSensorsDataGrid.SelectedItem is not SelectedOverlaySensorViewModel selectedSensor)
            return;

        _viewModel.RemoveSelectedSensor(selectedSensor);
        UpdateStatus();
    }

    private void MoveSelectedSensorUp_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSensorsDataGrid.SelectedItem is not SelectedOverlaySensorViewModel selectedSensor)
            return;

        _viewModel.MoveSelectedSensorUp(selectedSensor);
        SelectedSensorsDataGrid.SelectedItem = selectedSensor;
        UpdateStatus();
    }

    private void MoveSelectedSensorDown_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSensorsDataGrid.SelectedItem is not SelectedOverlaySensorViewModel selectedSensor)
            return;

        _viewModel.MoveSelectedSensorDown(selectedSensor);
        SelectedSensorsDataGrid.SelectedItem = selectedSensor;
        UpdateStatus();
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Save();
        UpdateStatus();
        System.Windows.MessageBox.Show("Settings saved.", "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void HideToTray_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureDataDirectories();
        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.DataDirectory,
            UseShellExecute = true
        });
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _host.RequestExit();
    }

    private void RefreshDetectedSensors()
    {
        try
        {
            _viewModel.RefreshDetectedSensors();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "OptiSensor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
