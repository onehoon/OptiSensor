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

    internal MainWindow(ApplicationHost host, SensorPublishService publishService, AppSettings settings)
    {
        InitializeComponent();

        _host = host;
        _publishService = publishService;
        _settings = settings;
        _publishService.StatusChanged += PublishService_StatusChanged;

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
        DetectedSensorCountTextBlock.Text = $"Detected sensors: {_publishService.LastDetectedSensorCount}";
        SelectedSensorCountTextBlock.Text = $"Selected sensors: {_settings.SelectedSensors.Count}";
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
}
