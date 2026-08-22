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
        var sensorsIndex = body.IndexOf("host.StartSensorServices();", StringComparison.Ordinal);

        Assert.True(conditionIndex >= 0,
            "Start() must keep MainWindow creation conditional on showMainWindow.");
        Assert.True(showCallIndex > conditionIndex,
            "host.ShowMainWindow() must remain inside/after the showMainWindow guard.");
        Assert.True(tweaksIndex >= 0 && sensorsIndex >= 0);
        Assert.True(tweaksIndex < sensorsIndex, "Tweaks must still start before Sensors.");
        Assert.True(sensorsIndex < conditionIndex,
            "Background sensor startup must not be gated by the UI branch.");
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

        Assert.Contains("_mainWindow is not null && !_mainWindow.TryPrepareForExit()", source);
        Assert.Contains("WaitForMainWindowTeardownAsync()", source);
        Assert.Contains("_mainWindow?.IsVisible == true", source);
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
