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
}
