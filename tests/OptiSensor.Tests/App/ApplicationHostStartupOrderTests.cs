using System.Runtime.CompilerServices;
using Xunit;

namespace OptiSensor.Tests.App;

/// <summary>
/// ApplicationHost.Start() wires up WPF (System.Windows.Application.Current, MainWindow, the tray
/// icon) and can't be exercised end-to-end from a headless xunit run without a live UI thread, so
/// this is a structural/source-level check rather than a behavioral one. It pins down the property
/// this branch changes: Tweaks startup is kicked off before sensor services.
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
    public void Start_InvokesStartTweaksInBackground_BeforeStartSensorServices()
    {
        var source = ReadApplicationHostSource();

        var startMethodStart = source.IndexOf("public static ApplicationHost Start(", StringComparison.Ordinal);
        Assert.True(startMethodStart >= 0, "Could not locate ApplicationHost.Start method.");

        // The method body is short; grab a generous slice and locate the closing brace of Start()
        // by finding the next top-level method declaration that follows it.
        var nextMemberStart = source.IndexOf("private void StartTweaksInBackground", startMethodStart, StringComparison.Ordinal);
        Assert.True(nextMemberStart > startMethodStart, "Could not bound the Start method body.");
        var body = source[startMethodStart..nextMemberStart];

        var tweaksCallIndex = body.IndexOf("host.StartTweaksInBackground();", StringComparison.Ordinal);
        var sensorsCallIndex = body.IndexOf("host.StartSensorServices();", StringComparison.Ordinal);

        Assert.True(tweaksCallIndex >= 0, "Start() must call host.StartTweaksInBackground().");
        Assert.True(sensorsCallIndex >= 0, "Start() must call host.StartSensorServices().");
        Assert.True(tweaksCallIndex < sensorsCallIndex,
            "Tweaks startup must be kicked off before sensor services, so Tweaks (e.g. Intel VRR " +
            "Range Fix) isn't gated on HWiNFO/sensor readiness at Windows boot.");
    }
}
