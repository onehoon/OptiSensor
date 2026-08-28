using System.Runtime.CompilerServices;
using OptiSensor.App;
using OptiSensor.Install;
using Xunit;

namespace OptiSensor.Tests.App;

public sealed class ElevationGateTests
{
    [Fact]
    public void CreateStartInfo_UsesRunAsAndPreservesArguments()
    {
        var args = new[] { "--startup", "--once", "value with spaces", "\"quoted\"" };

        var startInfo = ElevationGate.CreateStartInfo("C:\\OptiSensor\\OptiSensor.exe", args);

        Assert.Equal("C:\\OptiSensor\\OptiSensor.exe", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("runas", startInfo.Verb);
        Assert.Equal(args, startInfo.ArgumentList);
    }

    [Fact]
    public void StartupTask_RequiresHighestAvailable()
    {
        Assert.Equal("HighestAvailable", StartupRegistration.RequiredRunLevel);

        var source = ReadStartupRegistrationSource();
        Assert.Contains("string.Equals(runLevel?.Trim(), RequiredRunLevel", source);
        Assert.Contains("new XElement(ns + \"RunLevel\", RequiredRunLevel)", source);
        Assert.DoesNotContain("LeastPrivilege", source);
    }

    [Fact]
    public void Main_RunsVelopackBeforeTheElevationGate()
    {
        var source = ReadAppSource();
        var velopackIndex = source.IndexOf("VelopackApp.Build().Run();", StringComparison.Ordinal);
        var elevationIndex = source.IndexOf("ElevationGate.IsRunningAsAdministrator()", StringComparison.Ordinal);

        Assert.True(velopackIndex >= 0);
        Assert.True(elevationIndex > velopackIndex);
    }

    private static string ReadAppSource([CallerFilePath] string thisFilePath = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFilePath)!, "..", "..", ".."));
        var path = Path.Combine(repoRoot, "src", "OptiSensor", "App", "App.xaml.cs");
        Assert.True(File.Exists(path), $"Expected to find App.xaml.cs at {path}");
        return File.ReadAllText(path);
    }

    private static string ReadStartupRegistrationSource([CallerFilePath] string thisFilePath = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFilePath)!, "..", "..", ".."));
        var path = Path.Combine(repoRoot, "src", "OptiSensor", "Install", "StartupRegistration.cs");
        Assert.True(File.Exists(path), $"Expected to find StartupRegistration.cs at {path}");
        return File.ReadAllText(path);
    }
}
