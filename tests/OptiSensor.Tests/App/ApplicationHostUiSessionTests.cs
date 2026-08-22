using System.Runtime.CompilerServices;
using Xunit;

namespace OptiSensor.Tests.App;

public sealed class ApplicationHostUiSessionTests
{
    private static string ReadApplicationHostSource([CallerFilePath] string thisFilePath = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFilePath)!, "..", "..", ".."));
        var path = Path.Combine(repoRoot, "src", "OptiSensor", "App", "ApplicationHost.cs");
        Assert.True(File.Exists(path), $"Expected to find ApplicationHost.cs at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void HidePathPreservesDirtySessionsAndTearsDownCleanSessions()
    {
        var source = ReadApplicationHostSource();

        Assert.Contains("window.HasUnsavedChanges", source);
        Assert.Contains("window.HidePreservingSession()", source);
        Assert.Contains("window.HideForSessionTeardown()", source);
        Assert.Contains("_mainWindowTeardownTask = TearDownMainWindowAsync(window)", source);
    }

    [Fact]
    public void TeardownClearsHostReferenceAndCanReopenAfterCompletion()
    {
        var source = ReadApplicationHostSource();

        Assert.Contains("await window.PrepareForSessionTeardownAsync()", source);
        Assert.Contains("window.CloseAfterSessionTeardown()", source);
        Assert.Contains("ReferenceEquals(_mainWindow, window)", source);
        Assert.Contains("_mainWindow = null", source);
        Assert.Contains("_showMainWindowAfterTeardown", source);
        Assert.Contains("private bool _mainWindowTeardownInProgress", source);
        Assert.Contains("if (reopen && !IsExitRequested && _shutdownTask is null && !_disposed)", source);

        var completeStart = source.IndexOf("private void CompleteMainWindowTeardown(", StringComparison.Ordinal);
        var completeEnd = source.IndexOf("private async Task ObserveSensorStartupCompletionAsync", completeStart, StringComparison.Ordinal);
        Assert.True(completeStart >= 0 && completeEnd > completeStart);
        var completion = source[completeStart..completeEnd];
        var markFinished = completion.IndexOf("MarkMainWindowTeardownFinished()", StringComparison.Ordinal);
        var reopenCall = completion.IndexOf("ShowMainWindow();", StringComparison.Ordinal);
        Assert.True(markFinished >= 0 && reopenCall > markFinished);
        Assert.Contains("_mainWindowTeardownTask = Task.CompletedTask", source);
    }

    [Fact]
    public void ShutdownWaitsForUiTeardownBeforeFinalCleanup()
    {
        var source = ReadApplicationHostSource();

        Assert.Contains("var windowShutdownTask = WaitForMainWindowTeardownAsync()", source);
        Assert.Contains("Task.WhenAll(windowShutdownTask, _mainWindow.PrepareForShutdownAsync())", source);
        Assert.Contains("await _mainWindowTeardownTask.ConfigureAwait(false)", source);
    }

    [Fact]
    public void TeardownFailureStillRetiresOldSession()
    {
        var source = ReadApplicationHostSource();

        Assert.Contains("Exception? cleanupFailure = null", source);
        Assert.Contains("CompleteMainWindowTeardown(window, cleanupFailure)", source);
        Assert.Contains("Failed to close retired MainWindow", source);
        Assert.Contains("_mainWindowTeardownInProgress = false", source);
        Assert.Contains("_mainWindow = null", source);
    }
}
