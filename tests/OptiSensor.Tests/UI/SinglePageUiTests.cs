using System.Runtime.CompilerServices;
using Xunit;

namespace OptiSensor.Tests.UI;

/// <summary>
/// MainWindow is WPF, so the single-page consolidation is pinned with source-level checks at the
/// architecture boundary: no multi-page navigation, the visible telemetry line comes from
/// shared-memory readback (not the publisher's internal <c>LastOverlayLine</c>), and there is no
/// manual update UI.
/// </summary>
public sealed class SinglePageUiTests
{
    private static string RepoRoot([CallerFilePath] string thisFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFilePath)!, "..", "..", ".."));

    private static string Src(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "OptiSensor", relativePath));

    [Fact]
    public void MultiPageNavigationIsRemoved()
    {
        var root = Path.Combine(RepoRoot(), "src", "OptiSensor", "UI");
        Assert.False(File.Exists(Path.Combine(root, "Views", "Pages", "OverlayPage.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "Views", "Pages", "SensorsPage.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "Views", "Pages", "SettingsPage.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "Views", "Pages", "TweaksPage.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "MainWindowViewModel.cs")));

        var xaml = Src(Path.Combine("UI", "MainWindow.xaml"));
        Assert.DoesNotContain("NavButton", xaml);
        Assert.DoesNotContain("PageContentControl", xaml);
        Assert.DoesNotContain("SensorsPage", xaml);
    }

    [Fact]
    public void WpfUiDependencyIsRemoved()
    {
        // The single native page uses only local styles + standard WPF controls, so the
        // WPF-UI package and its resource dictionaries must not be loaded at startup.
        Assert.DoesNotContain("WPF-UI", Src("OptiSensor.csproj"));
        Assert.DoesNotContain("Wpf.Ui", Src("App.xaml"));
        Assert.DoesNotContain("Wpf.Ui", Src(Path.Combine("UI", "MainWindow.xaml")));
    }

    [Fact]
    public void VisibleTelemetryLineComesFromSharedMemoryReadbackNotLastOverlayLine()
    {
        var code = Src(Path.Combine("UI", "MainWindow.xaml.cs"));

        Assert.Contains("ExternalOverlayReader", code);
        Assert.Contains("_overlayReader.TryReadLine()", code);
        Assert.Contains("OverlayFeedText.Text", code);
        Assert.DoesNotContain("LastOverlayLine", code);
        Assert.DoesNotContain("SensorPublishService", code);

        // 1 s preview timer, and it must never trigger a hardware sample.
        Assert.Contains("TimeSpan.FromMilliseconds(1000)", code);
        Assert.DoesNotContain("SampleCore", code);
        Assert.DoesNotContain("SampleBattery", code);
    }

    [Fact]
    public void NoManualUpdateUiRemains_ButBackgroundUpdateCheckStays()
    {
        var window = Src(Path.Combine("UI", "MainWindow.xaml"));
        var code = Src(Path.Combine("UI", "MainWindow.xaml.cs"));
        Assert.DoesNotContain("Check for updates", window);
        Assert.DoesNotContain("CheckForUpdates", code);
        Assert.DoesNotContain("GitHubUpdateService", code);

        Assert.Contains("CheckForUpdatesInBackgroundAsync", Src(Path.Combine("App", "ApplicationHost.cs")));
    }

    [Fact]
    public void StartWithWindowsAndIntelVrrCardsRemain()
    {
        var window = Src(Path.Combine("UI", "MainWindow.xaml"));
        var code = Src(Path.Combine("UI", "MainWindow.xaml.cs"));

        Assert.Contains("Start with Windows", window);
        Assert.Contains("Intel VRR Range Fix", window);

        // Start with Windows applies immediately (no draft/save workflow).
        Assert.Contains("StartupRegistration.Register()", code);
        Assert.Contains("StartupRegistration.Unregister()", code);
        Assert.DoesNotContain("GeneralSettingsDraft", code);
        Assert.DoesNotContain("HasUnsavedChanges", code);

        // Intel VRR toggle only persists the flag.
        Assert.Contains("_settings.IntelVrrRangeFixEnabled =", code);
        Assert.DoesNotContain("IntelVrrRangeTweak", code);
    }

    [Fact]
    public void OptiScalerReplaceIsTheSecondCardAndOpensAModalNotADllPicker()
    {
        var xaml = Src(Path.Combine("UI", "MainWindow.xaml"));
        var code = Src(Path.Combine("UI", "MainWindow.xaml.cs"));

        // Card order: Current Overlay Feed -> OptiScaler Replace -> Intel VRR Range Fix -> Start with Windows.
        var feed = xaml.IndexOf("Current Overlay Feed", StringComparison.Ordinal);
        var replace = xaml.IndexOf("OptiScaler Replace", StringComparison.Ordinal);
        var vrr = xaml.IndexOf("Intel VRR Range Fix", StringComparison.Ordinal);
        var startup = xaml.IndexOf("Start with Windows", StringComparison.Ordinal);
        Assert.True(feed >= 0 && feed < replace && replace < vrr && vrr < startup);

        // "Manage" button lives in the card (right side), not the bottom action row.
        Assert.Contains("Content=\"Manage\"", xaml);
        Assert.Contains("Click=\"ManageOptiScalerButton_Click\"", xaml);

        // The Manage handler opens the modal as an owned dialog.
        Assert.Contains("new OptiScalerReplaceWindow { Owner = this }.ShowDialog()", code);

        // The dialog picks a folder (classic folder tree) and delegates to the existing core - no
        // DLL picker, no duplicated discovery, no hard-coded download URL.
        var dialog = Src(Path.Combine("UI", "OptiScalerReplaceWindow.xaml.cs"));
        Assert.Contains("FolderBrowserDialog", dialog);
        Assert.Contains("ShowNewFolderButton = false", dialog);
        Assert.Contains("AutoUpgradeEnabled = false", dialog); // classic expandable folder tree
        Assert.DoesNotContain("OpenFileDialog", dialog);
        Assert.DoesNotContain("OpenFolderDialog", dialog);
        Assert.Contains("_discovery.Discover(", dialog);
        Assert.Contains("_updateService.UpdateAsync(", dialog);
        Assert.DoesNotContain("github.com", dialog);
        Assert.DoesNotContain("0.10", dialog);

        // Folder picker: preselect the current folder, cancel is a no-op, success runs discovery.
        Assert.Contains("picker.SelectedPath = _selectedFolder", dialog);
        Assert.Contains("!= WinForms.DialogResult.OK)", dialog);
        var pick = dialog.IndexOf("FolderBrowserDialog", StringComparison.Ordinal);
        var cancelReturn = dialog.IndexOf("return; // cancel", pick, StringComparison.Ordinal);
        var assignSelected = dialog.IndexOf("_selectedFolder = picker.SelectedPath", pick, StringComparison.Ordinal);
        var runDiscovery = dialog.IndexOf("RunDiscovery();", assignSelected, StringComparison.Ordinal);
        Assert.True(cancelReturn > pick && cancelReturn < assignSelected && runDiscovery > assignSelected);
    }

    [Fact]
    public void ObsoleteDirtySessionContractIsRemovedFromMainWindow()
    {
        var code = Src(Path.Combine("UI", "MainWindow.xaml.cs"));

        // All settings apply immediately, so the unsaved-draft lifecycle hooks are gone.
        Assert.DoesNotContain("TryPrepareForExit", code);
        Assert.DoesNotContain("ShouldPreserveSessionOnHide", code);
        Assert.DoesNotContain("HidePreservingSession", code);

        // The real deferred-teardown hooks remain.
        Assert.Contains("HideForSessionTeardown", code);
        Assert.Contains("CloseAfterSessionTeardown", code);
        Assert.Contains("PrepareForSessionTeardownAsync", code);
        Assert.Contains("_overlayReader.Dispose()", code);
    }

    [Fact]
    public void ImmediateApplyToggles_RevertOnPersistenceFailure()
    {
        var code = Src(Path.Combine("UI", "MainWindow.xaml.cs"));

        // Persistence success/failure is explicit, not fire-and-forget.
        Assert.Contains("private bool TrySaveSettings(string context, out string? error)", code);

        // Intel VRR: restore the previous flag and toggle when the save fails.
        Assert.Contains("if (!TrySaveSettings(\"Intel VRR Range Fix toggle\", out _))", code);
        Assert.Contains("_settings.IntelVrrRangeFixEnabled = previous;", code);
        Assert.Contains("IntelVrrToggle.IsChecked = previous;", code);

        // Start with Windows: best-effort roll the Task Scheduler mutation back so the
        // durable task and settings.json cannot silently diverge.
        Assert.Contains("if (!TrySaveSettings(\"Start with Windows toggle\", out var saveError))", code);
        Assert.Contains("var rollback = previous ? StartupRegistration.Register() : StartupRegistration.Unregister();", code);
        Assert.Contains("_settings.StartWithWindows = previous;", code);
        Assert.Contains("StartWithWindowsToggle.IsChecked = previous;", code);
    }
}
