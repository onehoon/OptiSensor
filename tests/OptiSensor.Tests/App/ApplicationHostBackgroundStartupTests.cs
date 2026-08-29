using System.Runtime.CompilerServices;
using Xunit;

namespace OptiSensor.Tests.App;

public sealed class ApplicationHostBackgroundStartupTests
{
    private static string ReadApplicationHostSource([CallerFilePath] string thisFilePath = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFilePath)!, "..", "..", ".."));
        var path = Path.Combine(repoRoot, "src", "OptiSensor", "App", "ApplicationHost.cs");
        Assert.True(File.Exists(path), $"Expected to find ApplicationHost.cs at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Constructor_DoesNotConstructMainWindow()
    {
        var source = ReadApplicationHostSource();
        var constructorStart = source.IndexOf("private ApplicationHost(", StringComparison.Ordinal);
        var startMethodStart = source.IndexOf("public static ApplicationHost Start(", StringComparison.Ordinal);

        Assert.InRange(constructorStart, 0, startMethodStart - 1);
        var constructor = source[constructorStart..startMethodStart];
        Assert.DoesNotContain("CreateMainWindow()", constructor);
        Assert.DoesNotContain("new MainWindow", constructor);
        Assert.DoesNotContain("_mainWindow =", constructor);
        Assert.Contains("private MainWindow CreateMainWindow()", source);
    }

    [Fact]
    public void Start_OnlyRequestsMainWindowWhenShowMainWindowIsTrue()
    {
        var source = ReadApplicationHostSource();
        var startMethodStart = source.IndexOf("public static ApplicationHost Start(", StringComparison.Ordinal);
        var nextMemberStart = source.IndexOf("private void StartTweaksInBackground", startMethodStart, StringComparison.Ordinal);

        Assert.True(startMethodStart >= 0, "Could not locate ApplicationHost.Start().");
        Assert.True(nextMemberStart > startMethodStart, "Could not bound ApplicationHost.Start().");

        var body = source[startMethodStart..nextMemberStart];
        var conditionIndex = body.IndexOf("if (showMainWindow)", StringComparison.Ordinal);
        var showCallIndex = body.IndexOf("host.ShowMainWindow();", StringComparison.Ordinal);
        var tweaksIndex = body.IndexOf("host.StartTweaksInBackground();", StringComparison.Ordinal);
        var sensorsIndex = body.IndexOf("host.StartPublishService();", StringComparison.Ordinal);

        Assert.True(conditionIndex >= 0,
            "Start() must keep MainWindow creation conditional on showMainWindow.");
        Assert.True(showCallIndex > conditionIndex,
            "host.ShowMainWindow() must remain inside/after the showMainWindow guard.");

        // Tweaks and the native publisher both start unconditionally, before the UI branch.
        // Their relative call order is deliberately not a contract - neither gates the other.
        Assert.True(tweaksIndex >= 0 && tweaksIndex < conditionIndex,
            "Tweaks startup must not be gated by the UI branch.");
        Assert.True(sensorsIndex >= 0 && sensorsIndex < conditionIndex,
            "Background publisher startup must not be gated by the UI branch.");
    }

    [Fact]
    public void MainWindowLifecycle_AllowsNoWindowUntilShowAndReusesIt()
    {
        var source = ReadApplicationHostSource();
        var showStart = source.IndexOf("public void ShowMainWindow()", StringComparison.Ordinal);
        var requestExitStart = source.IndexOf("public void RequestExit()", showStart, StringComparison.Ordinal);
        var showMethod = source[showStart..requestExitStart];

        Assert.Contains("if (_mainWindow is null)", showMethod);
        Assert.Contains("_mainWindow = CreateMainWindow()", showMethod);
        Assert.DoesNotContain("_mainWindow = new MainWindow", showMethod);
        Assert.Contains("_mainWindow.IsVisible", showMethod);
    }

    [Fact]
    public void ExitVisibilityAndShutdown_HandleMissingMainWindow()
    {
        var source = ReadApplicationHostSource();

        // Shutdown and the update visibility check must both tolerate a null MainWindow
        // (startup mode / already torn down to the tray).
        Assert.Contains("WaitForMainWindowTeardownAsync()", source);
        Assert.Contains("if (_mainWindow is not null)", source);
        Assert.Contains("_mainWindow?.IsVisible == true", source);

        // The obsolete unsaved-draft exit gate is gone.
        Assert.DoesNotContain("TryPrepareForExit", source);
    }

    [Fact]
    public void BackgroundUpdate_RestartIsGuardedByExitStateAfterTheVisibilityHop()
    {
        var source = ReadApplicationHostSource();
        var start = source.IndexOf("private async Task CheckForUpdatesInBackgroundAsync()", StringComparison.Ordinal);
        var end = source.IndexOf("private Task<bool> IsMainWindowVisibleAsync()", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var body = source[start..end];

        var visibilityHop = body.IndexOf("await IsMainWindowVisibleAsync()", StringComparison.Ordinal);
        var finalGuard = body.LastIndexOf("IsExitCleanupInProgress(", StringComparison.Ordinal);
        var apply = body.IndexOf("GitHubUpdateService.ApplyAndRestart(", StringComparison.Ordinal);

        Assert.True(visibilityHop >= 0, "The visibility check must remain.");
        Assert.True(finalGuard > visibilityHop && apply > finalGuard,
            "An explicit Exit during the visibility hop must cancel the update restart: the exit " +
            "check has to run after the await and immediately before ApplyAndRestart.");

        // The existing application-lifetime token flows into the update work; shutdown cancellation
        // is treated as normal, not an update failure.
        Assert.Contains("DownloadLatestAsync(", body);
        Assert.Contains("shutdownToken", body);
        Assert.Contains("catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)", body);
    }

    [Fact]
    public void ShowMainWindow_LazyCreationOccursOnlyAfterLifetimeGuard()
    {
        var source = ReadApplicationHostSource();
        var showStart = source.IndexOf("public void ShowMainWindow()", StringComparison.Ordinal);
        var requestExitStart = source.IndexOf("public void RequestExit()", showStart, StringComparison.Ordinal);
        Assert.True(showStart >= 0 && requestExitStart > showStart);
        var showMethod = source[showStart..requestExitStart];

        var secondGuard = showMethod.LastIndexOf("IsUiCreationBlocked(dispatcher)", StringComparison.Ordinal);
        var creation = showMethod.IndexOf("CreateMainWindow()", StringComparison.Ordinal);

        Assert.True(secondGuard >= 0, "Expected a UI-thread lifetime guard before lazy window creation.");
        Assert.True(creation > secondGuard,
            "MainWindow creation must happen only after the post-dispatch lifetime guard.");
    }
}
