using System.Runtime.CompilerServices;
using Xunit;

namespace OptiSensor.Tests.UI;

public sealed class LazyUiLifecycleTests
{
    private static string ReadSource(string relativePath, [CallerFilePath] string thisFilePath = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFilePath)!, "..", "..", ".."));
        var path = Path.Combine(repoRoot, "src", "OptiSensor", relativePath);
        Assert.True(File.Exists(path), $"Expected to find source at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void MainWindow_ConstructsOnlyOverlayPageInitially()
    {
        var source = ReadSource(Path.Combine("UI", "MainWindow.xaml.cs"));
        var constructorStart = source.IndexOf("internal MainWindow(", StringComparison.Ordinal);
        var constructorEnd = source.IndexOf("private SensorsPage GetOrCreateSensorsPage", constructorStart, StringComparison.Ordinal);
        Assert.True(constructorStart >= 0 && constructorEnd > constructorStart);
        var constructor = source[constructorStart..constructorEnd];

        Assert.Contains("new MainWindowViewModel", constructor);
        Assert.Contains("new OverlayPage", constructor);
        Assert.DoesNotContain("new SensorsPage", constructor);
        Assert.DoesNotContain("new TweaksPage", constructor);
        Assert.DoesNotContain("new SettingsPage", constructor);
        Assert.DoesNotContain("StartSensorRefreshWhenReady();", constructor);
    }

    [Fact]
    public void NavigationCreatesPagesOnDemandAndRefreshRequiresActiveSensorsPage()
    {
        var source = ReadSource(Path.Combine("UI", "MainWindow.xaml.cs"));

        Assert.Contains("NavigateTo(GetOrCreateSensorsPage(), SensorsNavButton)", source);
        Assert.Contains("var page = GetOrCreateTweaksPage()", source);
        Assert.Contains("NavigateTo(GetOrCreateSettingsPage(), SettingsNavButton)", source);
        Assert.Contains("!IsSensorsPageActive", source);
        Assert.Contains("_sensorRefreshTimer?", source);
    }

    [Fact]
    public void ViewModel_CreatesDiscoveryServiceOnlyWhenRefreshRuns()
    {
        var source = ReadSource(Path.Combine("UI", "MainWindowViewModel.cs"));
        var constructorStart = source.IndexOf("public MainWindowViewModel(", StringComparison.Ordinal);
        var constructorEnd = source.IndexOf("public event PropertyChangedEventHandler?", constructorStart, StringComparison.Ordinal);
        Assert.True(constructorStart >= 0 && constructorEnd > constructorStart);
        var constructor = source[constructorStart..constructorEnd];

        Assert.DoesNotContain("new SensorDiscoveryService", constructor);
        Assert.Contains("_sensorDiscoveryService ??= new SensorDiscoveryService", source);
        Assert.Contains("_sensorDiscoveryService?.Dispose()", source);
    }

    [Fact]
    public void OverlaySaveUsesPersistedGeneralSettingsWhenSettingsPageWasNeverCreated()
    {
        var source = ReadSource(Path.Combine("UI", "MainWindow.xaml.cs"));

        Assert.Contains("TryGetGeneralSettingsDraft", source);
        Assert.Contains("_settingsPage is not null", source);
        Assert.Contains("_settings.StartWithWindows", source);
        Assert.Contains("_settingsPage?.AcceptSavedDraft", source);
    }

    [Fact]
    public void SettingsUpdateCheckIsTrackedByWindowLifetime()
    {
        var source = ReadSource(Path.Combine("UI", "MainWindow.xaml.cs"));

        Assert.Contains("private Task _activeUpdateCheckTask", source);
        Assert.Contains("SettingsPage_CheckForUpdatesRequested", source);
        Assert.Contains("_activeUpdateCheckTask = task", source);
        Assert.Contains("await _activeUpdateCheckTask", source);
        Assert.Contains("_windowLifetimeCancellation.Token", source);
        Assert.Contains("cancellationToken.IsCancellationRequested", source);
        Assert.Contains("GitHubUpdateService.ApplyAndRestart(result)", source);
    }

    [Fact]
    public void ManualUpdateCheckPassesCancellationToUpdateService()
    {
        var mainWindow = ReadSource(Path.Combine("UI", "MainWindow.xaml.cs"));
        var updateService = ReadSource(Path.Combine("Updates", "GitHubUpdateService.cs"));

        Assert.Contains("}, cancellationToken);", mainWindow);
        Assert.Contains("CancellationToken cancellationToken = default", updateService);
        Assert.Contains(".WaitAsync(cancellationToken)", updateService);

        var downloadStart = updateService.IndexOf("await manager.DownloadUpdatesAsync(", StringComparison.Ordinal);
        var downloadEnd = updateService.IndexOf(".ConfigureAwait(false);", downloadStart, StringComparison.Ordinal);
        Assert.True(downloadStart >= 0 && downloadEnd > downloadStart);
        Assert.Contains("cancellationToken", updateService[downloadStart..downloadEnd]);
    }
}
