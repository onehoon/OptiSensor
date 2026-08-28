using System.Runtime.CompilerServices;
using System.Text;
using OptiSensor.Claw;
using OptiSensor.Overlay;
using Xunit;

namespace OptiSensor.Tests.Publishing;

/// <summary>
/// The runtime publisher wiring depends on the WPF application/dispatcher and native runtime
/// libraries, so the authority switch is pinned with structural source-level checks (matching
/// the existing ApplicationHost startup-order tests) plus a real formatter/protocol-length check.
/// </summary>
public class NativePublisherWiringTests
{
    private static string ReadSource(string relativePath, [CallerFilePath] string thisFilePath = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFilePath)!, "..", "..", ".."));
        var path = Path.Combine(repoRoot, "src", "OptiSensor", relativePath);
        Assert.True(File.Exists(path), $"Expected source at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void ApplicationHost_NormalStartupPublishesWithoutWaitingForHwInfo()
    {
        var source = ReadSource(Path.Combine("App", "ApplicationHost.cs"));

        var startServicesStart = source.IndexOf("private void StartSensorServices()", StringComparison.Ordinal);
        var startServicesEnd = source.IndexOf('}', source.IndexOf('{', startServicesStart));
        var startServices = source[startServicesStart..startServicesEnd];

        Assert.Contains("StartPublishService();", startServices);
        Assert.DoesNotContain("EnsureRunningAndWaitForSharedMemoryAsync", startServices);
        Assert.DoesNotContain("StartHwInfoAndPublishWhenReadyAsync", startServices);

        Assert.Contains("var publishService = new SensorPublishService();", source);

        // The native publish start must not re-raise the legacy HWiNFO readiness signal.
        var startPublishStart = source.IndexOf("private void StartPublishService()", StringComparison.Ordinal);
        var startPublishEnd = source.IndexOf("private bool IsExitCleanupInProgress", startPublishStart, StringComparison.Ordinal);
        var startPublish = source[startPublishStart..startPublishEnd];
        Assert.DoesNotContain("SensorSourceReady?.Invoke", startPublish);
        Assert.DoesNotContain("IsSensorSourceReady = true", startPublish);
    }

    [Fact]
    public void SensorPublishService_LoopUsesNativeTelemetryNotHwInfoRunner()
    {
        var source = ReadSource(Path.Combine("Publishing", "SensorPublishService.cs"));

        Assert.Contains("public SensorPublishService()", source);
        Assert.Contains("new ClawTelemetrySampler()", source);
        Assert.Contains("ClawTelemetryFormatter.Format(", source);
        Assert.Contains("new ExternalOverlayPublisher()", source);

        Assert.DoesNotContain("HwInfoSensorReader", source);
        Assert.DoesNotContain("SensorPublishRunner", source);
        Assert.DoesNotContain("OverlayOutputComposer", source);
        Assert.DoesNotContain("Func<SensorPublishRunner>", source);
    }

    [Fact]
    public void RepresentativeNativeLineFitsExternalOverlayProtocol()
    {
        var snapshot = new ClawTelemetrySnapshot(
            CpuUsagePercent: 36, CpuTemperatureC: 67, CpuPackagePowerW: 18,
            GpuUsagePercent: 98, GpuClockMHz: 2300,
            SystemMemoryUsedBytes: 20UL * 1024 * 1024 * 1024,
            GpuMemoryUsedBytes: (ulong)(9.4 * 1024 * 1024 * 1024),
            FanRpm: 3540, BatteryPercent: 72, OnBattery: true, RemainingMinutes: 150);

        var line = ClawTelemetryFormatter.Format(snapshot);

        Assert.Equal(
            "CPU 36% 67°C | GPU 98% 2300MHz | TDP 18W | RAM 20.0GB | VRAM 9.4GB | FAN 3540RPM | BAT 72% 2.5h",
            line);
        Assert.True(
            Encoding.UTF8.GetByteCount(line) <= ExternalOverlayProtocol.MaxLineLength - 1,
            $"Native line is {Encoding.UTF8.GetByteCount(line)} UTF-8 bytes; protocol allows {ExternalOverlayProtocol.MaxLineLength - 1}.");
    }
}
