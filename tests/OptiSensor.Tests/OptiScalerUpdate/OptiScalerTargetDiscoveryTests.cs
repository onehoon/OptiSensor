using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using OptiSensor.OptiScalerUpdate;
using Xunit;

namespace OptiSensor.Tests.OptiScalerUpdate;

/// <summary>
/// Folder-based OptiScaler target discovery: breadth-first through the selected game-folder tree,
/// identity by exact <c>ProductName</c>/<c>FileDescription</c> == "OptiScaler" (never by filename),
/// 0.9-only, first supported target closest to the root wins, and a discovery-vs-update failure
/// policy (an unreadable DLL or inaccessible subfolder is skipped, not fatal).
/// </summary>
public sealed class OptiScalerTargetDiscoveryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "optisensor-discovery", Guid.NewGuid().ToString("N"));

    public OptiScalerTargetDiscoveryTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (Exception) { /* best effort */ }
    }

    // ---- helpers -----------------------------------------------------------

    private static OptiScalerFileVersion V(string? productName = "OptiScaler", string? fileDescription = null, string fileVersion = "0.9.5.3")
    {
        var parts = Version.Parse(fileVersion);
        return new OptiScalerFileVersion(parts.Major, parts.Minor, Math.Max(0, parts.Build), Math.Max(0, parts.Revision),
            productName, "internal", "orig.dll", fileDescription, fileVersion);
    }

    /// <summary>Creates an (empty) DLL file at <paramref name="relativePath"/> under the game
    /// folder, making parent directories as needed. Returns its full path.</summary>
    private string Dll(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(_folder, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0]);
        return path;
    }

    private OptiScalerDiscoveryResult Discover(Dictionary<string, OptiScalerFileVersion?> byPath) =>
        DiscoverWith(new PathVersionReader(byPath));

    private OptiScalerDiscoveryResult DiscoverWith(IFileVersionReader reader) =>
        new OptiScalerTargetDiscovery(reader).Discover(_folder);

    // ---- identity contract (preserved from the top-level-only version) ---

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
        Assert.Equal(OptiScalerDiscoveryStatus.Found, Discover(new() { [dll] = V(productName: productName) }).Status);
    }

    [Theory]
    [InlineData("OptiScaler Helper")]
    [InlineData("MyOptiScaler")]
    [InlineData("Custom OptiScaler Build")]
    public void Rejects_substring_identity_false_positives(string name)
    {
        var dll = Dll("dxgi.dll");
        Assert.Equal(OptiScalerDiscoveryStatus.NotFound, Discover(new() { [dll] = V(productName: name, fileDescription: name) }).Status);
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
        Assert.Equal(OptiScalerDiscoveryStatus.NotFound,
            Discover(new() { [dll] = V(productName: "Direct3D", fileDescription: "Direct3D") }).Status);
    }

    // ---- breadth-first subfolder search ---------------------------------

    [Fact]
    public void Finds_a_root_level_target()
    {
        var dll = Dll("dxgi.dll");
        Assert.Equal(dll, Discover(new() { [dll] = V() }).TargetDllPath);
    }

    [Fact]
    public void Finds_a_one_level_child_target()
    {
        var dll = Dll("Binaries/dxgi.dll");
        var result = Discover(new() { [dll] = V() });
        Assert.Equal(OptiScalerDiscoveryStatus.Found, result.Status);
        Assert.Equal(dll, result.TargetDllPath);
    }

    [Fact]
    public void Finds_a_deep_target()
    {
        var dll = Dll("Binaries/Win64/dxgi.dll");
        var result = Discover(new() { [dll] = V() });
        Assert.Equal(OptiScalerDiscoveryStatus.Found, result.Status);
        Assert.Equal(dll, result.TargetDllPath);
    }

    [Fact]
    public void A_shallower_target_wins_over_a_deeper_one()
    {
        var deep = Dll("A/Deep/deep.dll");
        var shallow = Dll("B/shallow.dll");
        var result = Discover(new() { [deep] = V(), [shallow] = V() });
        Assert.Equal(OptiScalerDiscoveryStatus.Found, result.Status);
        Assert.Equal(shallow, result.TargetDllPath);
    }

    [Fact]
    public void Traversal_stops_at_the_first_supported_target_and_does_not_read_deeper_dlls()
    {
        var target = Dll("aaa/target.dll");       // depth 1, alphabetically first
        var deeper = Dll("zzz/Deep/Deeper/x.dll"); // depth 3, must never be inspected
        var recorder = new RecordingVersionReader(new PathVersionReader(new() { [target] = V(), [deeper] = V() }));

        var result = DiscoverWith(recorder);

        Assert.Equal(target, result.TargetDllPath);
        Assert.Contains(Path.GetFullPath(target), recorder.ReadPaths);
        Assert.DoesNotContain(Path.GetFullPath(deeper), recorder.ReadPaths);
    }

    [Fact]
    public void An_unsupported_optiscaler_earlier_in_the_walk_does_not_block_a_valid_0_9_target()
    {
        var tenTools = Dll("Tools/optiscaler.dll");
        var nineDeep = Dll("Binaries/Win64/dxgi.dll");
        var result = Discover(new()
        {
            [tenTools] = V(fileVersion: "0.10.2.0"),
            [nineDeep] = V(fileVersion: "0.9.5.3"),
        });
        Assert.Equal(OptiScalerDiscoveryStatus.Found, result.Status);
        Assert.Equal(nineDeep, result.TargetDllPath);
    }

    [Fact]
    public void An_unsupported_only_tree_reports_UnsupportedVersion_with_the_detected_path()
    {
        var ten = Dll("Tools/optiscaler.dll");
        var result = Discover(new() { [ten] = V(fileVersion: "0.10.2.0") });
        Assert.Equal(OptiScalerDiscoveryStatus.UnsupportedVersion, result.Status);
        Assert.Equal(ten, result.TargetDllPath);
        Assert.Equal(new Version(0, 10, 2, 0), result.Version);
    }

    [Fact]
    public void Multiple_identity_matches_in_one_directory_return_MultipleFound()
    {
        var a = Dll("Binaries/dxgi.dll");
        var b = Dll("Binaries/winmm.dll");
        var result = Discover(new() { [a] = V(), [b] = V(fileDescription: "OptiScaler", productName: "x") });
        Assert.Equal(OptiScalerDiscoveryStatus.MultipleFound, result.Status);
        Assert.Null(result.TargetDllPath);
        Assert.Equal(2, result.DetectedPaths.Count);
        Assert.Contains(a, result.DetectedPaths);
        Assert.Contains(b, result.DetectedPaths);
    }

    [Fact]
    public void An_unreadable_dll_does_not_block_a_valid_target_elsewhere_in_the_tree()
    {
        var broken = Dll("bin/broken.dll");
        var valid = Dll("bin/x64/winmm.dll");
        var result = Discover(new() { [broken] = null, [valid] = V() });
        Assert.Equal(OptiScalerDiscoveryStatus.Found, result.Status);
        Assert.Equal(valid, result.TargetDllPath);
    }

    [Fact]
    public void An_inaccessible_subfolder_does_not_block_a_valid_target_in_another_branch()
    {
        var blocked = Directory.CreateDirectory(Path.Combine(_folder, "blocked")).FullName;
        Dll("blocked/hidden.dll");
        var valid = Dll("ok/dxgi.dll");

        if (!TryDenyListAccess(blocked))
            return; // environment doesn't honor the Deny ACE; the other 20+ cases still cover the walk

        try
        {
            var result = Discover(new() { [valid] = V() });
            Assert.Equal(OptiScalerDiscoveryStatus.Found, result.Status);
            Assert.Equal(valid, result.TargetDllPath);
        }
        finally
        {
            TryRestoreAccess(blocked);
        }
    }

    [Fact]
    public void A_reparse_point_child_directory_is_not_followed()
    {
        // The only OptiScaler DLL lives OUTSIDE the selected tree; a junction inside the tree is the
        // only path to it. Skipping reparse points must therefore yield NotFound (and not loop).
        var outside = Path.Combine(Path.GetTempPath(), "optisensor-discovery-outside", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var outsideDll = Path.Combine(outside, "dxgi.dll");
        File.WriteAllBytes(outsideDll, [0]);
        var junction = Path.Combine(_folder, "link");

        try
        {
            Assert.True(TryCreateJunction(junction, outside),
                "could not create a junction for the reparse-point test");

            var result = Discover(new() { [outsideDll] = V() });
            Assert.Equal(OptiScalerDiscoveryStatus.NotFound, result.Status);
        }
        finally
        {
            try { Directory.Delete(junction); } catch (Exception) { }
            try { Directory.Delete(outside, recursive: true); } catch (Exception) { }
        }
    }

    // ---- edges ----------------------------------------------------------

    [Fact]
    public void Empty_tree_is_NotFound() => Assert.Equal(OptiScalerDiscoveryStatus.NotFound, Discover(new()).Status);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_path_is_InvalidFolder(string path) =>
        Assert.Equal(OptiScalerDiscoveryStatus.InvalidFolder,
            new OptiScalerTargetDiscovery(new PathVersionReader(new())).Discover(path).Status);

    [Fact]
    public void Missing_root_is_InvalidFolder() =>
        Assert.Equal(OptiScalerDiscoveryStatus.InvalidFolder,
            new OptiScalerTargetDiscovery(new PathVersionReader(new())).Discover(Path.Combine(_folder, "nope")).Status);

    // ---- filesystem helpers -------------------------------------------------

    private static bool TryCreateJunction(string link, string target)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            p.WaitForExit(10_000);
            return p.ExitCode == 0 && Directory.Exists(link);
        }
        catch { return false; }
    }

    private static bool TryDenyListAccess(string directory)
    {
        try
        {
            var info = new DirectoryInfo(directory);
            var security = info.GetAccessControl();
            var user = WindowsIdentity.GetCurrent().User!;
            security.AddAccessRule(new FileSystemAccessRule(
                user,
                FileSystemRights.ListDirectory | FileSystemRights.ReadData,
                AccessControlType.Deny));
            info.SetAccessControl(security);
            try { _ = Directory.EnumerateFileSystemEntries(directory).ToList(); return false; }
            catch (UnauthorizedAccessException) { return true; }
        }
        catch { return false; }
    }

    private static void TryRestoreAccess(string directory)
    {
        try
        {
            var info = new DirectoryInfo(directory);
            var security = info.GetAccessControl();
            var user = WindowsIdentity.GetCurrent().User!;
            security.RemoveAccessRule(new FileSystemAccessRule(
                user,
                FileSystemRights.ListDirectory | FileSystemRights.ReadData,
                AccessControlType.Deny));
            info.SetAccessControl(security);
        }
        catch { /* Dispose does a best-effort recursive delete */ }
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

    private sealed class RecordingVersionReader(IFileVersionReader inner) : IFileVersionReader
    {
        public List<string> ReadPaths { get; } = [];

        public OptiScalerFileVersion Read(string path)
        {
            ReadPaths.Add(Path.GetFullPath(path));
            return inner.Read(path);
        }
    }
}
