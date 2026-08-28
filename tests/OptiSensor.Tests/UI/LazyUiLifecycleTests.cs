using System.Runtime.CompilerServices;
using OptiSensor.Models;
using OptiSensor.Settings;
using OptiSensor.UI;
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
        Assert.Contains("UpdateSelectedSensorRuntimeValues", source);
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

    [Fact]
    public void OverlayPreviewUsesRuntimeSnapshotWithoutPopulatingDetectedSensors()
    {
        var settings = new AppSettings
        {
            HwInfoProfile = new SensorSourceProfile
            {
                OverlayGroups =
                [
                new OverlayGroup
                {
                    Id = "gpu",
                    Name = "GPU",
                    Order = 0,
                    Enabled = true,
                    Sensors =
                    [
                        new SelectedOverlaySensor
                        {
                            SensorId = "gpu-temp",
                            HardwareType = "GpuNvidia",
                            HardwareName = "GPU",
                            SensorType = "Temperature",
                            SensorName = "GPU Core",
                            Category = OptiSensorCategory.Gpu,
                            DisplayName = "GPU",
                            Unit = "°C",
                            Format = "{0:0}C",
                            Order = 0,
                            Enabled = true
                        }
                    ]
                }
                ]
            }
        };

        using var viewModel = new MainWindowViewModel(settings);
        var runtimeSensors = new[]
        {
            new DetectedSensorInfo(
                "gpu-temp", "GpuNvidia", "GPU", "Temperature", "GPU Core",
                OptiSensorCategory.Gpu, "°C", 64f)
        };

        viewModel.UpdateSelectedSensorRuntimeValues(runtimeSensors);
        var preview = viewModel.GetOverlayPreviewText(runtimeSensors);

        Assert.Contains("64", preview);
        Assert.DoesNotContain("Not found", viewModel.SelectedSensors[0].CurrentValueText,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, viewModel.DetectedSensorCount);
    }

    [Fact]
    public void EmptyRuntimeSnapshotClearsPreviouslyAvailableSelectedSensorValue()
    {
        var settings = new AppSettings
        {
            HwInfoProfile = new SensorSourceProfile
            {
                OverlayGroups =
                [
                    new OverlayGroup
                    {
                        Id = "gpu",
                        Name = "GPU",
                        Order = 0,
                        Enabled = true,
                        Sensors =
                        [
                            new SelectedOverlaySensor
                            {
                                SensorId = "gpu-temp",
                                HardwareType = "GpuNvidia",
                                HardwareName = "GPU",
                                SensorType = "Temperature",
                                SensorName = "GPU Core",
                                Category = OptiSensorCategory.Gpu,
                                DisplayName = "GPU",
                                Unit = "°C",
                                Format = "{0:0}C",
                                Order = 0,
                                Enabled = true
                            }
                        ]
                    }
                ]
            }
        };

        using var viewModel = new MainWindowViewModel(settings);
        viewModel.UpdateSelectedSensorRuntimeValues(
        [
            new DetectedSensorInfo(
                "gpu-temp", "GpuNvidia", "GPU", "Temperature", "GPU Core",
                OptiSensorCategory.Gpu, "°C", 64f)
        ]);

        Assert.Contains("64", viewModel.SelectedSensors[0].CurrentValueText);
        Assert.True(viewModel.SelectedSensors[0].IsAvailable);

        viewModel.UpdateSelectedSensorRuntimeValues(Array.Empty<DetectedSensorInfo>());

        Assert.Equal("Not found", viewModel.SelectedSensors[0].CurrentValueText);
        Assert.False(viewModel.SelectedSensors[0].IsAvailable);
    }

    [Fact]
    public void RuntimeSnapshotBridgeAvoidsAdditionalArrayCopies()
    {
        var runner = ReadSource(Path.Combine("Publishing", "SensorPublishRunner.cs"));
        var service = ReadSource(Path.Combine("Publishing", "SensorPublishService.cs"));

        Assert.DoesNotContain("effectiveSnapshot.Sensors.ToArray()", runner);
        Assert.DoesNotContain("result.Sensors.ToArray()", service);
        Assert.Contains("public IReadOnlyList<DetectedSensorInfo>? LastSensors", service);
    }

    [Fact]
    public void PublicUpdatesDoNotRequireTokenStateOrCredentialInput()
    {
        var mainWindow = ReadSource(Path.Combine("UI", "MainWindow.xaml.cs"));
        var settingsPage = ReadSource(Path.Combine("UI", "Views", "Pages", "SettingsPage.xaml.cs"));
        var settingsPageMarkup = ReadSource(Path.Combine("UI", "Views", "Pages", "SettingsPage.xaml"));
        var updateService = ReadSource(Path.Combine("Updates", "GitHubUpdateService.cs"));
        var host = ReadSource(Path.Combine("App", "ApplicationHost.cs"));

        Assert.DoesNotContain("HasPendingCredentialInput", settingsPage);
        Assert.DoesNotContain("GitHubToken", settingsPage);
        Assert.DoesNotContain("GitHubToken", settingsPageMarkup);
        Assert.DoesNotContain("Credential Manager", settingsPageMarkup);
        Assert.Contains("Text=\"Updates\"", settingsPageMarkup);
        Assert.Contains("Content=\"Check for updates\"", settingsPageMarkup);
        Assert.Contains("new GithubSource(RepositoryUrl, null, prerelease: false)", updateService);
        Assert.DoesNotContain("GitHubTokenStore", updateService);
        Assert.DoesNotContain("NoToken", updateService);
        Assert.Contains("internal bool ShouldPreserveSessionOnHide", mainWindow);
        Assert.Contains("internal bool ShouldPreserveSessionOnHide => IsDirty();", mainWindow);
        Assert.DoesNotContain("HasPendingCredentialInput", mainWindow);
        Assert.DoesNotContain("SaveGitHubToken", mainWindow);
        Assert.DoesNotContain("RemoveGitHubToken", mainWindow);
        Assert.Contains("window.ShouldPreserveSessionOnHide", host);

        var isDirtyStart = mainWindow.IndexOf("private bool IsDirty()", StringComparison.Ordinal);
        var tryPrepareForExitStart = mainWindow.IndexOf("internal bool TryPrepareForExit()", isDirtyStart, StringComparison.Ordinal);
        Assert.True(isDirtyStart >= 0 && tryPrepareForExitStart > isDirtyStart);
        Assert.DoesNotContain("HasPendingCredentialInput", mainWindow[isDirtyStart..tryPrepareForExitStart]);
    }
}
