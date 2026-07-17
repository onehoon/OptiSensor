using System.Globalization;
using System.Windows;
using OptiSensor.Settings;
using OptiSensor.Models;

namespace OptiSensor.UI.Views.Pages;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        SensorSourceComboBox.ItemsSource = Enum.GetValues<SensorSourceKind>();
    }

    public event EventHandler? SaveRequested;
    public event EventHandler? OpenSettingsFolderRequested;
    public event EventHandler? HideRequested;
    public event EventHandler? ExitRequested;

    internal void LoadSettings(AppSettings settings)
    {
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        SensorSourceComboBox.SelectedItem = settings.SensorSource;
        PublishIntervalTextBox.Text = settings.ClampedPublishIntervalMs.ToString(CultureInfo.InvariantCulture);
    }

    internal bool ApplySettingsEdits(AppSettings settings, out string? errorMessage)
    {
        errorMessage = null;
        if (!int.TryParse(PublishIntervalTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var publishIntervalMs))
        {
            errorMessage = "Publish interval must be a whole number.";
            return false;
        }

        settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        if (SensorSourceComboBox.SelectedItem is SensorSourceKind source)
            settings.SensorSource = source;
        settings.PublishIntervalMs = Math.Clamp(publishIntervalMs, 100, 10000);
        PublishIntervalTextBox.Text = settings.PublishIntervalMs.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    internal void UpdateRuntime(AppSettings settings, MainWindowViewModel viewModel)
    {
        RuntimeTextBlock.Text =
            $"Start with Windows: {(settings.StartWithWindows ? "Enabled" : "Disabled")}\n" +
            $"Publish interval: {settings.ClampedPublishIntervalMs} ms\n" +
            $"Detected sensors: {viewModel.DetectedSensorCount}\n" +
            $"Selected sensors: {viewModel.EnabledSelectedSensorCount} / {viewModel.TotalSelectedSensorCount}";
        SettingsStateTextBlock.Text = $"Settings: {viewModel.SettingsStateText}";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenSettingsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsFolderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        HideRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}
