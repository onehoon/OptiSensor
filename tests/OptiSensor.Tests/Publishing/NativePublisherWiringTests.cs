using System.Runtime.CompilerServices;
using System.Text;
using OptiSensor.Claw;
using OptiSensor.Overlay;
using Xunit;

namespace OptiSensor.Tests.Publishing;

/// <summary>
/// The authority switch is pinned with a minimal source-level check that no HWiNFO
/// infrastructure remains, plus real end-to-end lifecycle coverage of the native
/// sampler/publisher and a protocol-length check.
/// </summary>
[Collection("ExternalOverlayMapping")]
public class NativePublisherWiringTests
{
    private static string RepoRoot([CallerFilePath] string thisFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFilePath)!, "..", "..", ".."));

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "OptiSensor", relativePath));

    [Fact]
    public void NormalStartupUsesNativeTelemetryAndHasNoHwInfoInfrastructure()
    {
        var host = ReadSource(Path.Combine("App", "ApplicationHost.cs"));

        Assert.Contains("var publishService = new SensorPublishService();", host);
        Assert.Contains("host.StartPublishService();", host);
        Assert.DoesNotContain("HWiNFO", host);
        Assert.DoesNotContain("EnsureRunningAndWaitForSharedMemoryAsync", host);
        Assert.DoesNotContain("SensorSourceReady", host);
        Assert.DoesNotContain("SensorSourceStartupFailed", host);
        Assert.DoesNotContain("CreatePublishRunner", host);

        var service = ReadSource(Path.Combine("Publishing", "SensorPublishService.cs"));
        Assert.Contains("new ClawTelemetrySampler()", service);
        Assert.Contains("ClawTelemetryFormatter.Format(sampler.Latest)", service);
        Assert.Contains("new ExternalOverlayPublisher()", service);
        Assert.DoesNotContain("HwInfoSensorReader", service);
        Assert.DoesNotContain("SensorPublishRunner", service);

        // The HWiNFO reader / startup configurator and package reference are gone.
        Assert.DoesNotContain("Hwinfo.SharedMemory.Net", ReadSource("OptiSensor.csproj"));
        Assert.False(Directory.Exists(Path.Combine(RepoRoot(), "src", "OptiSensor", "HWiNFO")), "src/OptiSensor/HWiNFO must be deleted.");
    }

    [Fact]
    public async Task StartsPublishesAndStopsCleanly()
    {
        // Real end-to-end lifecycle on Windows: PDH CPU + GlobalMemoryStatusEx RAM are available on
        // any runner without MSI EC / Intel GPU, and ExternalOverlayPublisher writes a local
        // memory-mapped file. Exercises priming, the dual-loop coordination, and teardown - no fakes.
        using var service = new OptiSensor.Publishing.SensorPublishService();
        service.Start();
        Assert.True(service.IsRunning);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (DateTime.UtcNow < deadline && service.LastOverlayLine is null && service.LastError is null)
            await Task.Delay(100);

        Assert.Null(service.LastError);
        Assert.False(string.IsNullOrEmpty(service.LastOverlayLine));

        await service.StopAsync();
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task StopDuringPrimingTearsDownPromptly()
    {
        // Cancelling while the session is still in the priming delay must not hang on the
        // 5-second retry backoff or leave the service running.
        using var service = new OptiSensor.Publishing.SensorPublishService();
        service.Start();
        await Task.Delay(200); // still inside the ~1 s priming window

        var stop = service.StopAsync();
        Assert.True(await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(3))) == stop,
            "StopAsync during priming must complete promptly, not wait out the retry backoff.");
        Assert.False(service.IsRunning);
        Assert.Null(service.LastError);
    }

    [Fact]
    public void PublisherUsesFixedOneSecondHeartbeatWithNoConfigurableInterval()
    {
        var source = ReadSource(Path.Combine("Publishing", "SensorPublishService.cs"));

        Assert.Contains("private const int PublishIntervalMs = 1000", source);
        Assert.Contains("Task.Delay(PublishIntervalMs, sessionToken)", source);
        Assert.DoesNotContain("UpdatePublishInterval", source);
        Assert.DoesNotContain("_publishIntervalMs", source);
        Assert.DoesNotContain("MinPublishIntervalMs", source);
    }

    [Fact]
    public void RepresentativeNativeLineFitsExternalOverlayProtocol()
    {
        var line = ClawTelemetryFormatter.Format(new ClawTelemetrySnapshot(
            CpuUsagePercent: 36, CpuTemperatureC: 67, CpuPackagePowerW: 18,
            GpuUsagePercent: 98, GpuClockMHz: 2300,
            SystemMemoryUsedBytes: 20UL * 1024 * 1024 * 1024,
            GpuMemoryUsedBytes: (ulong)(9.4 * 1024 * 1024 * 1024),
            FanRpm: 3540, BatteryPercent: 72, OnBattery: true, RemainingMinutes: 150));

        Assert.Equal(
            "CPU 36% 67°C | GPU 98% 2300MHz | TDP 18W | RAM 20.0GB | VRAM 9.4GB | FAN 3540RPM | BAT 72% 2.5h",
            line);
        Assert.True(Encoding.UTF8.GetByteCount(line) <= ExternalOverlayProtocol.MaxLineLength - 1);
    }
}
