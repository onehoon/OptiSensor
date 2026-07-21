using System.Globalization;
using System.Windows;
using OptiSensor.Settings;
using OptiSensor.Models;

namespace OptiSensor.UI.Views.Pages;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    private bool _isLoadingSettings;
    private bool _hasUnsavedChanges;

    public SettingsPage()
    {
        InitializeComponent();
        SensorSourceComboBox.ItemsSource = Enum.GetValues<SensorSourceKind>();

        SensorSourceComboBox.SelectionChanged += OnSensorSourceSelectionChanged;
        StartWithWindowsCheckBox.Checked += OnStartWithWindowsChanged;
        StartWithWindowsCheckBox.Unchecked += OnStartWithWindowsChanged;
        PublishIntervalComboBox.SelectionChanged += OnPublishIntervalSelectionChanged;
    }

    public event EventHandler? SaveRequested;
    public event EventHandler? OpenSettingsFolderRequested;
    public event EventHandler? HideRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<string>? SaveGitHubTokenRequested;
    public event EventHandler? RemoveGitHubTokenRequested;
    public event EventHandler? CheckForUpdatesRequested;

    internal event EventHandler? EditsChanged;

    internal bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (_hasUnsavedChanges == value)
                return;

            _hasUnsavedChanges = value;
            EditsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void LoadSettings(AppSettings settings)
    {
        LoadControls(settings.StartWithWindows, settings.SensorSource, settings.ClampedPublishIntervalMs);
    }

    /// <summary>
    /// Re-baselines the controls to a draft that was just saved. Uses the draft
    /// (not the live AppSettings) because the sensor source in the draft may not
    /// yet match the live source when a source change requires an app restart.
    /// </summary>
    internal void AcceptSavedDraft(GeneralSettingsDraft draft)
    {
        LoadControls(draft.StartWithWindows, draft.SensorSource, draft.PublishIntervalMs);
    }

    internal bool TryCreateDraft(out GeneralSettingsDraft? draft, out string? errorMessage)
    {
        draft = null;
        errorMessage = null;

        if (PublishIntervalComboBox.SelectedValue is not string selectedInterval ||
            !int.TryParse(selectedInterval, NumberStyles.Integer, CultureInfo.InvariantCulture, out var publishIntervalMs))
        {
            errorMessage = "Select a publish interval.";
            return false;
        }

        if (SensorSourceComboBox.SelectedItem is not SensorSourceKind sensorSource)
        {
            errorMessage = "Select a sensor source.";
            return false;
        }

        draft = new GeneralSettingsDraft(
            StartWithWindowsCheckBox.IsChecked == true,
            sensorSource,
            Math.Clamp(publishIntervalMs, 100, 2000));
        return true;
    }

    private void LoadControls(bool startWithWindows, SensorSourceKind sensorSource, int publishIntervalMs)
    {
        _isLoadingSettings = true;
        try
        {
            StartWithWindowsCheckBox.IsChecked = startWithWindows;
            SensorSourceComboBox.SelectedItem = sensorSource;
            PublishIntervalComboBox.SelectedValue = publishIntervalMs.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _isLoadingSettings = false;
        }

        HasUnsavedChanges = false;
    }

    private void OnSensorSourceSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => MarkEdited();

    private void OnStartWithWindowsChanged(object sender, RoutedEventArgs e) => MarkEdited();

    private void OnPublishIntervalSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => MarkEdited();

    private void MarkEdited()
    {
        if (_isLoadingSettings)
            return;

        HasUnsavedChanges = true;
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
