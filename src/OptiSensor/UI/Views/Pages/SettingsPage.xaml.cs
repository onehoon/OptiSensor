using System.Globalization;
using System.Windows;
using OptiSensor.Settings;

namespace OptiSensor.UI.Views.Pages;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    private bool _isLoadingSettings;
    private bool _hasUnsavedChanges;
    private GeneralSettingsDraft? _baseline;

    public SettingsPage()
    {
        InitializeComponent();

        StartWithWindowsCheckBox.Checked += OnStartWithWindowsChanged;
        StartWithWindowsCheckBox.Unchecked += OnStartWithWindowsChanged;
        PublishIntervalComboBox.SelectionChanged += OnPublishIntervalSelectionChanged;
    }

    public event EventHandler? SaveRequested;
    public event EventHandler? OpenSettingsFolderRequested;
    public event EventHandler? HideRequested;
    public event EventHandler? ExitRequested;
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
        LoadControls(settings.StartWithWindows, settings.ClampedPublishIntervalMs);
    }

    /// <summary>Re-baselines the controls to a draft that was just saved.</summary>
    internal void AcceptSavedDraft(GeneralSettingsDraft draft)
    {
        LoadControls(draft.StartWithWindows, draft.PublishIntervalMs);
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

        draft = new GeneralSettingsDraft(
            StartWithWindowsCheckBox.IsChecked == true,
            Math.Clamp(publishIntervalMs, 100, 1000));
        return true;
    }

    private void LoadControls(bool startWithWindows, int publishIntervalMs)
    {
        _isLoadingSettings = true;
        try
        {
            StartWithWindowsCheckBox.IsChecked = startWithWindows;
            PublishIntervalComboBox.SelectedValue = publishIntervalMs.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _isLoadingSettings = false;
        }

        _baseline = new GeneralSettingsDraft(startWithWindows, publishIntervalMs);
        HasUnsavedChanges = false;
    }

    private void OnStartWithWindowsChanged(object sender, RoutedEventArgs e) => RecalculateDirty();

    private void OnPublishIntervalSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => RecalculateDirty();

    /// <summary>
    /// Compares the current control values against the last-loaded/last-saved
    /// baseline instead of just flagging "touched at least once", so toggling a
    /// control back to its saved value (e.g. 500 -> 1000 -> 500) clears dirty
    /// instead of leaving a false-positive unsaved-changes state.
    /// </summary>
    private void RecalculateDirty()
    {
        if (_isLoadingSettings)
            return;

        if (!TryCreateDraft(out var current, out _))
        {
            HasUnsavedChanges = true;
            return;
        }

        HasUnsavedChanges = current != _baseline;
    }

    internal void SetUpdateStatus(string message) => UpdateStateTextBlock.Text = message;

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

    private void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);
    }
}
