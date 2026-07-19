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
    public event EventHandler<string>? SaveGitHubTokenRequested;
    public event EventHandler? RemoveGitHubTokenRequested;
    public event EventHandler? CheckForUpdatesRequested;

    internal void LoadSettings(AppSettings settings)
    {
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        SensorSourceComboBox.SelectedItem = settings.SensorSource;
        PublishIntervalComboBox.SelectedValue = settings.ClampedPublishIntervalMs.ToString(CultureInfo.InvariantCulture);
    }

    internal bool ApplySettingsEdits(AppSettings settings, out string? errorMessage)
    {
        errorMessage = null;
        if (PublishIntervalComboBox.SelectedValue is not string selectedInterval ||
            !int.TryParse(selectedInterval, NumberStyles.Integer, CultureInfo.InvariantCulture, out var publishIntervalMs))
        {
            errorMessage = "Select a publish interval.";
            return false;
        }

        settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        if (SensorSourceComboBox.SelectedItem is SensorSourceKind source)
            settings.SensorSource = source;
        settings.PublishIntervalMs = Math.Clamp(publishIntervalMs, 100, 2000);
        return true;
    }

    internal void UpdateGitHubTokenState(bool hasToken, string? message = null)
    {
        GitHubTokenStateTextBlock.Text = message ?? (hasToken
            ? "A token is stored in Windows Credential Manager. The update feed is not configured yet."
            : "No token is stored. This is optional until a private update feed is configured.");
        GitHubTokenPasswordBox.Password = string.Empty;
    }

    internal void SetUpdateCheckInProgress(bool isInProgress)
    {
        CheckForUpdatesButton.IsEnabled = !isInProgress;
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

    private void SaveGitHubTokenButton_Click(object sender, RoutedEventArgs e)
    {
        SaveGitHubTokenRequested?.Invoke(this, GitHubTokenPasswordBox.Password);
    }

    private void RemoveGitHubTokenButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveGitHubTokenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);
    }
}
