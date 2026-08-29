using System.Runtime.CompilerServices;
using Xunit;

namespace OptiSensor.Tests.App;

/// <summary>
/// ApplicationHost.Start() depends on the WPF application/dispatcher and tray lifecycle, so the
/// startup ordering is pinned with a structural source-level check rather than an end-to-end
/// headless WPF test. MainWindow construction is intentionally lazy and is covered separately by
/// ApplicationHostBackgroundStartupTests.
/// </summary>
public class ApplicationHostStartupOrderTests
{
    private static string ReadApplicationHostSource([CallerFilePath] string thisFilePath = "")
    {
        // tests/OptiSensor.Tests/App/ -> tests/OptiSensor.Tests/ -> tests/ -> repo root
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFilePath)!, "..", "..", ".."));
        var path = Path.Combine(repoRoot, "src", "OptiSensor", "App", "ApplicationHost.cs");
        Assert.True(File.Exists(path), $"Expected to find ApplicationHost.cs at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Start_InvokesStartSensorServices()
    {
        var source = ReadApplicationHostSource();

        var startMethodStart = source.IndexOf("public static ApplicationHost Start(", StringComparison.Ordinal);
        Assert.True(startMethodStart >= 0, "Could not locate ApplicationHost.Start method.");

        // The method body is short; grab a generous slice and locate the closing brace of Start()
        // by finding the next top-level method declaration that follows it.
        var nextMemberStart = source.IndexOf("private void StartSensorServices", startMethodStart, StringComparison.Ordinal);
        Assert.True(nextMemberStart > startMethodStart, "Could not bound the Start method body.");
        var body = source[startMethodStart..nextMemberStart];

        var sensorsCallIndex = body.IndexOf("host.StartSensorServices();", StringComparison.Ordinal);

        Assert.True(sensorsCallIndex >= 0, "Start() must call host.StartSensorServices().");
    }
}
