using System.Runtime.CompilerServices;
using Xunit;

namespace OptiSensor.Tests.App;

/// <summary>
/// ApplicationHost.Start() wires up WPF (System.Windows.Application.Current, MainWindow, the tray
/// icon) and can't be exercised end-to-end from a headless xunit run without a live UI thread, so
/// these are structural/source-level checks rather than behavioral ones. They pin down the two
/// properties this branch changes: Tweaks startup is kicked off before sensor services, and
/// neither path awaits the other (both stay fire-and-forget and independent).
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

    [Fact]
    public void StartTweaksInBackground_And_StartSensorServices_DoNotAwaitEachOther()
    {
        var source = ReadApplicationHostSource();

        var tweaksMethodStart = source.IndexOf("private void StartTweaksInBackground()", StringComparison.Ordinal);
        var sensorsMethodStart = source.IndexOf("private void StartSensorServices()", StringComparison.Ordinal);
        Assert.True(tweaksMethodStart >= 0);
        Assert.True(sensorsMethodStart >= 0);

        // StartTweaksInBackground is a single fire-and-forget statement: "_ = TweakStartupCoordinator.RunAsync(...)".
        var tweaksBody = source[tweaksMethodStart..source.IndexOf('}', source.IndexOf('{', tweaksMethodStart))];
        Assert.DoesNotContain("StartSensorServices", tweaksBody, StringComparison.Ordinal);
        Assert.DoesNotContain("await", tweaksBody, StringComparison.Ordinal);
        Assert.Contains("_ = TweakStartupCoordinator.RunAsync", tweaksBody, StringComparison.Ordinal);

        // StartSensorServices must never reference the Tweaks coordinator or await its task.
        var nextMemberStart = source.IndexOf("private async Task StartHwInfoAndPublishWhenReadyAsync", sensorsMethodStart, StringComparison.Ordinal);
        Assert.True(nextMemberStart > sensorsMethodStart);
        var sensorsBody = source[sensorsMethodStart..nextMemberStart];
        Assert.DoesNotContain("TweakStartupCoordinator", sensorsBody, StringComparison.Ordinal);
        Assert.DoesNotContain("StartTweaksInBackground", sensorsBody, StringComparison.Ordinal);
    }
}
