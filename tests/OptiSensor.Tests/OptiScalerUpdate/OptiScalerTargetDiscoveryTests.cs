using OptiSensor.OptiScalerUpdate;
using Xunit;

namespace OptiSensor.Tests.OptiScalerUpdate;

/// <summary>
/// Folder-based OptiScaler target discovery: top-level only, identity by exact
/// <c>ProductName</c>/<c>FileDescription</c> == "OptiScaler" (never by filename), 0.9-only, and a
/// discovery-vs-update failure-policy split (an unreadable unrelated DLL is skipped, not fatal).
/// </summary>
public sealed class OptiScalerTargetDiscoveryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "optisensor-discovery", Guid.NewGuid().ToString("N"));

    public OptiScalerTargetDiscoveryTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static OptiScalerFileVersion V(string? productName = null, string? fileDescription = null, string fileVersion = "0.9.5.3")
    {
        var parts = Version.Parse(fileVersion);
        return new OptiScalerFileVersion(parts.Major, parts.Minor, Math.Max(0, parts.Build), Math.Max(0, parts.Revision),
            productName, "internal", "orig.dll", fileDescription, fileVersion);
    }

    private string Dll(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, [0]);
        return path;
    }

    private OptiScalerDiscoveryResult Discover(Dictionary<string, OptiScalerFileVersion?> byPath) =>
        new OptiScalerTargetDiscovery(new PathVersionReader(byPath)).Discover(_folder);

    [Fact]
    public void Finds_OptiScaler_by_exact_ProductName()
    {
        var dll = Dll("dxgi.dll");
        var result = Discover(new() { [dll] = V(productName: "OptiScaler", fileDescription: "something else") });
        Assert.Equal(OptiScalerDiscoveryStatus.Found, result.Status);
        Assert.Equal(dll, result.TargetDllPath);
        Assert.Equal(new Version(0, 9, 5, 3), result.Version);
    }

    [Fact]
    public void Finds_OptiScaler_by_exact_FileDescription()
    {
        var dll = Dll("dxgi.dll");
        var result = Discover(new() { [dll] = V(productName: "something else", fileDescription: "OptiScaler") });
        Assert.Equal(OptiScalerDiscoveryStatus.Found, result.Status);
        Assert.Equal(dll, result.TargetDllPath);
    }

    [Theory]
    [InlineData("optiscaler")]
    [InlineData("OPTISCALER")]
    [InlineData("  OptiScaler ")]
    public void Identity_is_case_insensitive_and_trimmed(string productName)
    {
        var dll = Dll("proxy.dll");
        var result = Discover(new() { [dll] = V(productName: productName) });
        Assert.Equal(OptiScalerDiscoveryStatus.Found, result.Status);
    }

    [Theory]
    [InlineData("OptiScaler Helper")]
    [InlineData("MyOptiScaler")]
    [InlineData("OptiScaler Proxy Helper")]
    [InlineData("Custom OptiScaler Build")]
    public void Rejects_substring_identity_false_positives(string name)
    {
        var dll = Dll("dxgi.dll");
        var result = Discover(new() { [dll] = V(productName: name, fileDescription: name) });
        Assert.Equal(OptiScalerDiscoveryStatus.NotFound, result.Status);
    }

    [Fact]
    public void Does_not_rely_on_filename_arbitrary_name_is_found()
    {
        var dll = Dll("abc123.dll");
        var result = Discover(new() { [dll] = V(productName: "OptiScaler") });
        Assert.Equal(OptiScalerDiscoveryStatus.Found, result.Status);
        Assert.Equal(dll, result.TargetDllPath);
    }

    [Fact]
    public void Does_not_rely_on_filename_proxy_name_without_metadata_is_not_found()
    {
        var dll = Dll("dxgi.dll");
        var result = Discover(new() { [dll] = V(productName: "Direct3D", fileDescription: "Direct3D") });
        Assert.Equal(OptiScalerDiscoveryStatus.NotFound, result.Status);
    }

    [Fact]
    public void Scans_top_level_only_and_does_not_recurse()
    {
        Dll("game.dll");
        var sub = Directory.CreateDirectory(Path.Combine(_folder, "subfolder")).FullName;
        var nested = Path.Combine(sub, "dxgi.dll");
        File.WriteAllBytes(nested, [0]);

        var result = Discover(new()
        {
            [Path.Combine(_folder, "game.dll")] = V(productName: "Game"),
            [nested] = V(productName: "OptiScaler"),
        });

        Assert.Equal(OptiScalerDiscoveryStatus.NotFound, result.Status);
    }

    [Fact]
    public void An_unreadable_unrelated_dll_does_not_abort_the_scan()
    {
        var broken = Dll("broken.dll");
        var opti = Dll("winmm.dll");
        var result = Discover(new()
        {
            [broken] = null, // version reader throws for this one
            [opti] = V(productName: "OptiScaler"),
        });
        Assert.Equal(OptiScalerDiscoveryStatus.Found, result.Status);
        Assert.Equal(opti, result.TargetDllPath);
    }

    [Fact]
    public void OptiScaler_of_an_unsupported_family_is_UnsupportedVersion_not_NotFound()
    {
        var dll = Dll("dxgi.dll");
        var result = Discover(new() { [dll] = V(productName: "OptiScaler", fileVersion: "0.10.2.0") });
        Assert.Equal(OptiScalerDiscoveryStatus.UnsupportedVersion, result.Status);
        Assert.Equal(dll, result.TargetDllPath);
        Assert.Equal(new Version(0, 10, 2, 0), result.Version);
    }

    [Fact]
    public void Multiple_OptiScaler_binaries_return_MultipleFound_and_pick_nothing()
    {
        var a = Dll("abc.dll");
        var b = Dll("xyz.dll");
        var result = Discover(new()
        {
            [a] = V(productName: "OptiScaler", fileVersion: "0.9.5.3"),
            [b] = V(fileDescription: "OptiScaler", fileVersion: "0.9.4.0"),
        });
        Assert.Equal(OptiScalerDiscoveryStatus.MultipleFound, result.Status);
        Assert.Null(result.TargetDllPath);
        Assert.Equal(2, result.DetectedPaths.Count);
        Assert.Contains(a, result.DetectedPaths);
        Assert.Contains(b, result.DetectedPaths);
    }

    [Fact]
    public void Empty_folder_is_NotFound()
    {
        Assert.Equal(OptiScalerDiscoveryStatus.NotFound, Discover(new()).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_path_is_InvalidFolder(string path)
    {
        var result = new OptiScalerTargetDiscovery(new PathVersionReader(new())).Discover(path);
        Assert.Equal(OptiScalerDiscoveryStatus.InvalidFolder, result.Status);
    }

    [Fact]
    public void Missing_folder_is_InvalidFolder()
    {
        var result = new OptiScalerTargetDiscovery(new PathVersionReader(new()))
            .Discover(Path.Combine(_folder, "no-such-subdir"));
        Assert.Equal(OptiScalerDiscoveryStatus.InvalidFolder, result.Status);
    }

    private sealed class PathVersionReader(Dictionary<string, OptiScalerFileVersion?> byPath) : IFileVersionReader
    {
        private readonly Dictionary<string, OptiScalerFileVersion?> _byPath = new(byPath, StringComparer.OrdinalIgnoreCase);

        public OptiScalerFileVersion Read(string path)
        {
            if (!_byPath.TryGetValue(Path.GetFullPath(path), out var version) && !_byPath.TryGetValue(path, out version))
                throw new FileNotFoundException($"No fake version data for '{path}'.");
            return version ?? throw new IOException($"Simulated unreadable metadata for '{path}'.");
        }
    }
}
