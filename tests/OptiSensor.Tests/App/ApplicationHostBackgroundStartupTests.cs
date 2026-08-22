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
    public void Constructor_StoresWindowFactoryWithoutConstructingMainWindow()
    {
        var source = ReadApplicationHostSource();
        var constructorStart = source.IndexOf("private ApplicationHost(", StringComparison.Ordinal);
        var startMethodStart = source.IndexOf("public static ApplicationHost Start(", StringComparison.Ordinal);

        Assert.InRange(constructorStart, 0, startMethodStart - 1);
        var constructor = source[constructorStart..startMethodStart];
        Assert.DoesNotContain("_mainWindow = new MainWindow", constructor);
        Assert.Contains("_mainWindowFactory", constructor);
    }

    [Fact]
    public void MainWindowLifecycle_AllowsNoWindowUntilShowAndReusesIt()
    {
        var source = ReadApplicationHostSource();
        var showStart = source.IndexOf("public void ShowMainWindow()", StringComparison.Ordinal);
        var requestExitStart = source.IndexOf("public void RequestExit()", showStart, StringComparison.Ordinal);
        var showMethod = source[showStart..requestExitStart];

        Assert.Contains("if (_mainWindow is null)", showMethod);
        Assert.Contains("_mainWindow = _mainWindowFactory", showMethod);
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
    public void ShowMainWindow_RechecksLifetimeBeforeLazyConstruction()
    {
        var source = ReadApplicationHostSource();
        var showStart = source.IndexOf("public void ShowMainWindow()", StringComparison.Ordinal);
        var requestExitStart = source.IndexOf("public void RequestExit()", showStart, StringComparison.Ordinal);
        var showMethod = source[showStart..requestExitStart];

        Assert.Equal(2, CountOccurrences(showMethod, "if (_disposed ||"));
        Assert.Contains("IsExitRequested ||", showMethod);
        Assert.Contains("dispatcher.HasShutdownStarted ||", showMethod);
        Assert.Contains("dispatcher.HasShutdownFinished)", showMethod);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;

        return count;
    }
}
