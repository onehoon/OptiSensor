using OptiSensor.Settings;
using OptiSensor.Tweaks;
using Xunit;

namespace OptiSensor.Tests.Tweaks;

public class TweakStartupCoordinatorTests
{
    /// <summary>
    /// The Tweaks backend must be startable purely from ApplicationHost's background kickoff,
    /// with no dependency on TweaksPage or any other UI code path. This test calls the coordinator
    /// exactly the way ApplicationHost does - a plain AppSettings instance and a cancellation
    /// token, no UI types involved anywhere in the call graph.
    /// </summary>
    [Fact]
    public async Task RunAsync_IsCallableStandalone_WithoutAnyUiDependency()
    {
        var settings = new AppSettings { IntelVrrRangeFixEnabled = false };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var exception = await Record.ExceptionAsync(() => TweakStartupCoordinator.RunAsync(settings, cts.Token));

        Assert.Null(exception);
    }
}
