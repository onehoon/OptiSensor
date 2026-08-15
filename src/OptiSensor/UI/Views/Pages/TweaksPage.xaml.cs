using System.Windows.Controls.Primitives;
using OptiSensor.App;
using OptiSensor.Settings;
using OptiSensor.Tweaks.IntelVrr;

namespace OptiSensor.UI.Views.Pages;

/// <summary>
/// Simple container of "tweak cards" so more tweaks can be added later without restructuring.
/// This page only reads the persisted toggle and the last-run result snapshot to render itself -
/// it never triggers a tweak run (no execution from the constructor, Loaded, navigation, or the
/// toggle's Click handler). The toggle only persists the boolean setting immediately.
/// </summary>
public partial class TweaksPage : System.Windows.Controls.UserControl
{
    private readonly AppSettings _settings;

    internal TweaksPage(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        IntelVrrToggle.IsChecked = _settings.IntelVrrRangeFixEnabled;
        RefreshFromDisk();
    }

    /// <summary>Re-reads the persisted last-run result from disk and updates the compact result
    /// line. Read-only - does not execute the tweak.</summary>
    internal void RefreshFromDisk()
    {
        var result = IntelVrrResultStore.TryLoad();
        IntelVrrResultText.Text = DescribeResult(result);

        if (result?.RangeBeforeText is not null && result.RangeAfterText is not null)
            IntelVrrRangeText.Text = $"{result.RangeBeforeText} -> {result.RangeAfterText}";
    }

    private void IntelVrrToggle_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var isEnabled = (sender as ToggleButton)?.IsChecked ?? false;
        _settings.IntelVrrRangeFixEnabled = isEnabled;

        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            SimpleLog.TryWrite($"Failed to save Intel VRR Range Fix toggle: {ex.Message}");
        }

        IntelVrrResultText.Text = "Applies at next startup.";
    }

    private static string DescribeResult(IntelVrrRunResult? result)
    {
        if (result is null)
            return "Not run yet. Applies at next startup.";

        return result.Status switch
        {
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
    }
}
