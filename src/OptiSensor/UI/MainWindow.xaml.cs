using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using OptiSensor.App;
using OptiSensor.Install;
using OptiSensor.Overlay;
using OptiSensor.Settings;
using OptiSensor.Tweaks.IntelVrr;

namespace OptiSensor.UI;

/// <summary>
/// The whole Claw UI: one page showing the live shared-memory overlay line, the Intel VRR Range
/// Fix card, and the Start-with-Windows toggle. Owns only UI-lifetime concerns - the shared-memory
/// preview reader/timer. It never owns the publish service or any hardware reader; background
/// publishing runs whether or not this window is open.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ApplicationHost _host;
    private readonly AppSettings _settings;

    private readonly ExternalOverlayReader _overlayReader = new();
    private readonly DispatcherTimer _previewTimer;
    private Task? _prepareLifetimeEndTask;
    private bool _allowPermanentClose;

    internal MainWindow(ApplicationHost host, AppSettings settings)
    {
        InitializeComponent();

        _host = host;
        _settings = settings;

        Title = $"OptiSensor v{GetApplicationVersion()}";
        VersionText.Text = $"Version {GetApplicationVersion()}";

        IntelVrrToggle.IsChecked = _settings.IntelVrrRangeFixEnabled;
        RefreshIntelVrrResult();

        StartWithWindowsToggle.IsChecked = _settings.StartWithWindows;
        UpdateStartWithWindowsStatus();

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _previewTimer.Tick += (_, _) => RefreshOverlayPreview();

        IsVisibleChanged += MainWindow_IsVisibleChanged;
        RefreshOverlayPreview();
    }

    // ---- shared-memory preview (UI-lifetime only) ------------------------

    private void MainWindow_IsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            RefreshIntelVrrResult();
            RefreshOverlayPreview();
            _previewTimer.Start();
        }
        else
        {
            _previewTimer.Stop();
        }
    }

    private void RefreshOverlayPreview()
    {
        var line = _overlayReader.TryReadLine();
        OverlayFeedText.Text = string.IsNullOrEmpty(line) ? "Waiting for telemetry..." : line;
    }

    // ---- Intel VRR Range Fix (persist-only; startup coordinator runs it) --

    private void IntelVrrToggle_Click(object sender, RoutedEventArgs e)
    {
        var previous = _settings.IntelVrrRangeFixEnabled;
        _settings.IntelVrrRangeFixEnabled = (sender as ToggleButton)?.IsChecked ?? false;

        if (!TrySaveSettings("Intel VRR Range Fix toggle", out _))
        {
            _settings.IntelVrrRangeFixEnabled = previous;
            IntelVrrToggle.IsChecked = previous;
        }
    }

    private void RefreshIntelVrrResult()
    {
        var result = IntelVrrResultStore.TryLoad();
        IntelVrrResultText.Text = DescribeIntelVrrResult(result);
        IntelVrrPanelNameText.Text = result?.PanelName is not null ? $"Panel: {result.PanelName}" : string.Empty;
        IntelVrrRangeText.Text = result?.RangeBeforeText is not null && result.RangeAfterText is not null
            ? $"{result.RangeBeforeText} -> {result.RangeAfterText}"
            : string.Empty;
    }

    private static string DescribeIntelVrrResult(IntelVrrRunResult? result) => result?.Status switch
    {
        null => "No result yet",
        IntelVrrRunStatus.Disabled => "Disabled.",
        IntelVrrRunStatus.Unavailable => "Unavailable: Intel graphics control library not accessible.",
        IntelVrrRunStatus.UnsupportedPanel => "This panel is not affected.",
        IntelVrrRunStatus.AmbiguousDisplay => "Skipped: multiple displays matched.",
        IntelVrrRunStatus.AlreadyCorrect => "Already using the native VRR range.",
        IntelVrrRunStatus.SkippedUserProfile => "Skipped: a custom profile is already set.",
        IntelVrrRunStatus.Applied => "Native VRR range restored.",
        IntelVrrRunStatus.ApplyFailed => $"Failed to apply: {result.Message}",
        IntelVrrRunStatus.VerificationFailed => "Applied but could not verify.",
        _ => result.Message
    };

    // ---- Start with Windows (immediate apply) ---------------------------

    private void StartWithWindowsToggle_Click(object sender, RoutedEventArgs e)
    {
        var previous = _settings.StartWithWindows;
        var enabled = (sender as ToggleButton)?.IsChecked ?? false;

        var apply = enabled ? StartupRegistration.Register() : StartupRegistration.Unregister();
        if (!apply.Success)
        {
            // Revert the toggle so the visible state matches the actual task state.
            StartWithWindowsToggle.IsChecked = previous;
            StartWithWindowsStatusText.Text = $"Could not update startup task: {apply.ErrorMessage}";
            return;
        }

        _settings.StartWithWindows = enabled;
        if (!TrySaveSettings("Start with Windows toggle", out var saveError))
        {
            // Persistence failed: restore the task to the previously persisted state so
            // settings.json and Task Scheduler do not silently diverge.
            var rollback = previous ? StartupRegistration.Register() : StartupRegistration.Unregister();
            _settings.StartWithWindows = previous;
            StartWithWindowsToggle.IsChecked = previous;
            StartWithWindowsStatusText.Text = rollback.Success
                ? $"Could not save the setting; startup change was reverted: {saveError}"
                : $"Could not save the setting and could not restore the startup task: {rollback.ErrorMessage}";
            return;
        }

        UpdateStartWithWindowsStatus();
    }

    private void UpdateStartWithWindowsStatus()
    {
        StartWithWindowsStatusText.Text = _settings.StartWithWindows
            ? "OptiSensor launches at sign-in."
            : "OptiSensor does not launch at sign-in.";
    }

    private bool TrySaveSettings(string context, out string? error)
    {
        try
        {
            _settings.Save();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            SimpleLog.TryWrite($"Failed to persist {context}: {ex.Message}");
            error = ex.Message;
            return false;
        }
    }

    // ---- window buttons -------------------------------------------------

    private void OpenSettingsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureDataDirectories();
        Process.Start(new ProcessStartInfo { FileName = AppPaths.DataDirectory, UseShellExecute = true });
    }

    private void HideButton_Click(object sender, RoutedEventArgs e) => _host.RequestHideMainWindow();

    private void ExitButton_Click(object sender, RoutedEventArgs e) => _host.RequestExit();

    // ---- tray / host lifecycle ----------------------------------------

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
        _previewTimer.Stop();
        IsVisibleChanged -= MainWindow_IsVisibleChanged;
        _overlayReader.Dispose();
        base.OnClosed(e);
    }

    /// <summary>Immediate-apply toggles mean there is never an unsaved draft to confirm on exit.</summary>
    internal bool TryPrepareForExit() => true;

    /// <summary>No unsaved edits, so hiding always retires this UI session (recreated on next show).</summary>
    internal bool ShouldPreserveSessionOnHide => false;

    internal void HidePreservingSession()
    {
        _previewTimer.Stop();
        Hide();
    }

    internal void HideForSessionTeardown()
    {
        _previewTimer.Stop();
        Hide();
    }

    internal void CloseAfterSessionTeardown()
    {
        _allowPermanentClose = true;
        Close();
    }

    internal Task PrepareForShutdownAsync() => PrepareForLifetimeEndAsync();

    internal Task PrepareForSessionTeardownAsync() => PrepareForLifetimeEndAsync();

    private Task PrepareForLifetimeEndAsync()
    {
        return _prepareLifetimeEndTask ??= Run();

        Task Run()
        {
            _previewTimer.Stop();
            IsVisibleChanged -= MainWindow_IsVisibleChanged;
            _overlayReader.Dispose();
            return Task.CompletedTask;
        }
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
}
