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
        Assert.DoesNotContain("_mainWindow = new MainWindow", constructor);
        Assert.Contains("private MainWindow CreateMainWindow()", source);
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
        Assert.Contains("_mainWindow?.PrepareForShutdownAsync() ?? Task.CompletedTask", source);
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
