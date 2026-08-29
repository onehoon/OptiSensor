using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using OptiSensor.OptiScalerUpdate;
using Xunit;

namespace OptiSensor.Tests.OptiScalerUpdate;

/// <summary>
/// Ports OptiEditor's OptiScaler replacement/validation coverage and adds the OptiSensor-specific
/// download path. Version metadata is faked (a real PE resource can't be fabricated on a temp file)
/// and the GitHub download is served from an in-memory handler, so every test is deterministic and
/// offline.
/// </summary>
public sealed class OptiScalerUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "optisensor-osc-tests", Guid.NewGuid().ToString("N"));

    public OptiScalerUpdateTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // ---- helpers -------------------------------------------------------------

    private static OptiScalerFileVersion OptiScaler09 => new(
        0, 9, 5, 3, "OptiScaler", "OptiScaler", "OptiScaler.dll", "OptiScaler", "0.9.5.3");

    private static OptiScalerFileVersion NotOptiScaler => new(
        1, 2, 3, 4, "Some Game DLL", null, "game.dll", "A game", "1.2.3.4");

    private static byte[] MakeZip(string entryName, byte[] content)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var entryStream = entry.Open();
            entryStream.Write(content);
        }
        return buffer.ToArray();
    }

    private static HttpClient HttpReturning(byte[] body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) })));

    private string WriteFile(string name, byte[] bytes)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Sha(byte[] bytes) => SHA256.HashData(bytes);

    // ---- source / binary validation ---------------------------------------

    [Theory]
    [InlineData("ProductName")]
    [InlineData("InternalName")]
    [InlineData("OriginalFilename")]
    [InlineData("FileDescription")]
    public void Validation_accepts_OptiScaler_identity_in_any_version_field(string field)
    {
        var path = WriteFile("candidate.bin", [1]);
        var version = new OptiScalerFileVersion(0, 9, 0, 0,
            field == "ProductName" ? "my OptiScaler build" : null,
            field == "InternalName" ? "OPTISCALER" : null,
            field == "OriginalFilename" ? "custom-OptiScaler.bin" : null,
            field == "FileDescription" ? "OptiScaler proxy" : null,
            "0.9.0.0");

        var result = new OptiScalerBinaryValidator(new FakeVersionReader(version)).Validate(path);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validation_ignores_the_filename()
    {
        var path = WriteFile("OptiScaler.dll", [1]);
        var result = new OptiScalerBinaryValidator(new FakeVersionReader(NotOptiScaler)).Validate(path);
        Assert.False(result.IsValid);
        Assert.Equal(OptiScalerBinaryProblem.NotOptiScaler, result.Problem);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    public void Validation_rejects_families_other_than_0_9(int major, int minor)
    {
        var path = WriteFile("candidate.bin", [1]);
        var version = OptiScaler09 with { Major = major, Minor = minor, FileVersionText = $"{major}.{minor}.0.0" };
        var result = new OptiScalerBinaryValidator(new FakeVersionReader(version)).Validate(path);
        Assert.False(result.IsValid);
        Assert.Equal(OptiScalerBinaryProblem.UnsupportedVersion, result.Problem);
    }

    [Fact]
    public void Validation_rejects_unreadable_numeric_version()
    {
        var path = WriteFile("candidate.bin", [1]);
        var version = OptiScaler09 with { FileVersionText = "not-a-version" };
        Assert.False(new OptiScalerBinaryValidator(new FakeVersionReader(version)).Validate(path).IsValid);
    }

    // ---- end-to-end update via OptiScalerUpdateService --------------------

    [Theory]
    [InlineData("dxgi.dll")]
    [InlineData("winmm.dll")]
    public async Task Update_replaces_the_target_bytes_and_keeps_its_filename(string proxyName)
    {
        var newBytes = new byte[] { 10, 20, 30, 40 };
        var target = WriteFile(proxyName, [1, 2]);
        var service = new OptiScalerUpdateService(
            new FakeVersionReader(byName: new() { ["OptiScaler.dll"] = OptiScaler09, [proxyName] = OptiScaler09 }),
            HttpReturning(MakeZip("OptiScaler.dll", newBytes)));

        var result = await service.UpdateAsync(target, CancellationToken.None);

        Assert.Equal(OptiScalerUpdateStatus.Replaced, result.Status);
        Assert.Equal(proxyName, Path.GetFileName(target));
        Assert.Equal(newBytes, await File.ReadAllBytesAsync(target));
        Assert.Empty(LeftoverTempFiles());
    }

    [Fact]
    public async Task Update_skips_when_the_target_already_has_the_downloaded_bytes()
    {
        var bytes = new byte[] { 7, 7, 7, 7 };
        var target = WriteFile("dxgi.dll", bytes);
        var service = new OptiScalerUpdateService(
            new FakeVersionReader(OptiScaler09), HttpReturning(MakeZip("OptiScaler.dll", bytes)));

        var result = await service.UpdateAsync(target, CancellationToken.None);

        Assert.Equal(OptiScalerUpdateStatus.Skipped, result.Status);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task Update_fails_when_the_target_is_missing_and_creates_nothing()
    {
        var target = Path.Combine(_root, "does-not-exist.dll");
        var service = new OptiScalerUpdateService(new FakeVersionReader(OptiScaler09), HttpReturning(MakeZip("OptiScaler.dll", [1])));

        var result = await service.UpdateAsync(target, CancellationToken.None);

        Assert.Equal(OptiScalerUpdateStatus.Failed, result.Status);
        Assert.Equal(OptiScalerUpdateReason.TargetMissing, result.Reason);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task Update_fails_when_the_target_is_not_an_OptiScaler_binary()
    {
        var original = new byte[] { 9, 9 };
        var target = WriteFile("dxgi.dll", original);
        var service = new OptiScalerUpdateService(
            new FakeVersionReader(byName: new() { ["OptiScaler.dll"] = OptiScaler09, ["dxgi.dll"] = NotOptiScaler }),
            HttpReturning(MakeZip("OptiScaler.dll", [1, 2, 3])));

        var result = await service.UpdateAsync(target, CancellationToken.None);

        Assert.Equal(OptiScalerUpdateStatus.Failed, result.Status);
        Assert.Equal(OptiScalerUpdateReason.TargetNotOptiScaler, result.Reason);
        Assert.Equal(original, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task Update_fails_when_the_target_is_not_OptiScaler_0_9()
    {
        var original = new byte[] { 9, 9 };
        var target = WriteFile("dxgi.dll", original);
        var service = new OptiScalerUpdateService(
            new FakeVersionReader(byName: new()
            {
                ["OptiScaler.dll"] = OptiScaler09,
                ["dxgi.dll"] = OptiScaler09 with { Minor = 10, FileVersionText = "0.10.0.0" },
            }),
            HttpReturning(MakeZip("OptiScaler.dll", [1, 2, 3])));

        var result = await service.UpdateAsync(target, CancellationToken.None);

        Assert.Equal(OptiScalerUpdateReason.UnsupportedTargetVersion, result.Reason);
        Assert.Equal(original, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task Update_fails_on_http_error_without_touching_the_target()
    {
        var original = new byte[] { 5, 6 };
        var target = WriteFile("dxgi.dll", original);
        var service = new OptiScalerUpdateService(
            new FakeVersionReader(OptiScaler09), HttpReturning([], HttpStatusCode.NotFound));

        var result = await service.UpdateAsync(target, CancellationToken.None);

        Assert.Equal(OptiScalerUpdateReason.DownloadFailed, result.Reason);
        Assert.Equal(original, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task Update_fails_on_a_corrupt_archive_without_touching_the_target()
    {
        var original = new byte[] { 5, 6 };
        var target = WriteFile("dxgi.dll", original);
        var service = new OptiScalerUpdateService(
            new FakeVersionReader(OptiScaler09), HttpReturning([1, 2, 3, 4, 5]));

        var result = await service.UpdateAsync(target, CancellationToken.None);

        Assert.Equal(OptiScalerUpdateReason.InvalidArchive, result.Reason);
        Assert.Equal(original, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task Update_fails_when_the_archive_has_no_root_OptiScaler_dll()
    {
        var original = new byte[] { 5, 6 };
        var target = WriteFile("dxgi.dll", original);
        var service = new OptiScalerUpdateService(
            new FakeVersionReader(OptiScaler09), HttpReturning(MakeZip("nested/OptiScaler.dll", [1, 2, 3])));

        var result = await service.UpdateAsync(target, CancellationToken.None);

        Assert.Equal(OptiScalerUpdateReason.InvalidArchive, result.Reason);
        Assert.Equal(original, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task Update_fails_when_the_downloaded_dll_is_not_a_valid_OptiScaler_0_9_build()
    {
        var original = new byte[] { 5, 6 };
        var target = WriteFile("dxgi.dll", original);
        var service = new OptiScalerUpdateService(
            new FakeVersionReader(byName: new() { ["OptiScaler.dll"] = NotOptiScaler, ["dxgi.dll"] = OptiScaler09 }),
            HttpReturning(MakeZip("OptiScaler.dll", [1, 2, 3])));

        var result = await service.UpdateAsync(target, CancellationToken.None);

        Assert.Equal(OptiScalerUpdateReason.SourceValidationFailed, result.Reason);
        Assert.Equal(original, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task Update_reports_FileInUse_and_does_not_replace_a_locked_target()
    {
        var original = new byte[] { 5, 6 };
        var target = WriteFile("dxgi.dll", original);
        var service = new OptiScalerUpdateService(
            new FakeVersionReader(OptiScaler09), HttpReturning(MakeZip("OptiScaler.dll", [1, 2, 3, 4])));

        using (File.Open(target, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = await service.UpdateAsync(target, CancellationToken.None);
            Assert.Equal(OptiScalerUpdateReason.FileInUse, result.Reason);
        }

        Assert.Equal(original, await File.ReadAllBytesAsync(target));
        Assert.Empty(LeftoverTempFiles());
    }

    [Fact]
    public async Task Update_is_canceled_during_download_without_touching_the_target()
    {
        var original = new byte[] { 5, 6 };
        var target = WriteFile("dxgi.dll", original);
        using var cts = new CancellationTokenSource();
        var http = new HttpClient(new StubHandler(async (_, token) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage();
        }));
        var service = new OptiScalerUpdateService(new FakeVersionReader(OptiScaler09), http);

        var result = await service.UpdateAsync(target, cts.Token);

        Assert.Equal(OptiScalerUpdateStatus.Canceled, result.Status);
        Assert.Equal(original, await File.ReadAllBytesAsync(target));
    }

    // ---- replacement service (final verification / rollback / cleanup) ----

    [Fact]
    public async Task Replacement_restores_the_original_when_final_verification_fails()
    {
        var original = new byte[] { 4, 5, 6 };
        var source = WriteFile("OptiScaler.dll", [1, 2, 3]);
        var target = WriteFile("dxgi.dll", original);
        // OptiScaler 0.9 on the first two reads of the target, then "not OptiScaler" for the
        // post-swap verification read -> final verification must fail and roll back.
        var reader = new FakeVersionReader(OptiScaler09) { FlipTargetAfter = 2, TargetName = "dxgi.dll", FlippedValue = NotOptiScaler };
        var service = new OptiScalerReplacementService(reader, new OptiScalerBinaryValidator(reader));

        var result = await service.ReplaceAsync(source, Sha([1, 2, 3]), target, CancellationToken.None);

        Assert.Equal(OptiScalerUpdateStatus.Failed, result.Status);
        Assert.Equal(OptiScalerUpdateReason.FinalVerificationFailed, result.Reason);
        Assert.Equal(original, await File.ReadAllBytesAsync(target));
        Assert.Contains("restored", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(LeftoverTempFiles());
    }

    [Fact]
    public async Task Replacement_leaves_no_temp_or_rollback_files_after_success()
    {
        var newBytes = new byte[] { 1, 2, 3 };
        var source = WriteFile("OptiScaler.dll", newBytes);
        var target = WriteFile("version.dll", [9]);
        var reader = new FakeVersionReader(OptiScaler09);
        var service = new OptiScalerReplacementService(reader, new OptiScalerBinaryValidator(reader));

        var result = await service.ReplaceAsync(source, Sha(newBytes), target, CancellationToken.None);

        Assert.Equal(OptiScalerUpdateStatus.Replaced, result.Status);
        Assert.Equal(newBytes, await File.ReadAllBytesAsync(target));
        Assert.Empty(LeftoverTempFiles());
    }

    private IEnumerable<string> LeftoverTempFiles() =>
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Where(p => p.Contains(".optisensor.tmp", StringComparison.Ordinal)
                     || p.Contains(".optisensor.rollback", StringComparison.Ordinal));

    // ---- fakes ----------------------------------------------------------------

    private sealed class FakeVersionReader : IFileVersionReader
    {
        private readonly OptiScalerFileVersion? _fixed;
        private readonly Dictionary<string, OptiScalerFileVersion>? _byName;
        private int _targetReads;

        public FakeVersionReader(OptiScalerFileVersion @fixed) => _fixed = @fixed;
        public FakeVersionReader(Dictionary<string, OptiScalerFileVersion> byName) =>
            _byName = new(byName, StringComparer.OrdinalIgnoreCase);

        public int FlipTargetAfter { get; init; } = int.MaxValue;
        public string? TargetName { get; init; }
        public OptiScalerFileVersion? FlippedValue { get; init; }

        public OptiScalerFileVersion Read(string path)
        {
            var name = Path.GetFileName(path);
            if (TargetName is not null && name.StartsWith(TargetName, StringComparison.OrdinalIgnoreCase)
                && ++_targetReads > FlipTargetAfter && FlippedValue is not null)
                return FlippedValue;

            if (_fixed is not null) return _fixed;
            if (_byName!.TryGetValue(name, out var exact)) return exact;
            foreach (var (key, value) in _byName)
                if (name.StartsWith(key, StringComparison.OrdinalIgnoreCase)) return value;
            throw new KeyNotFoundException($"No fake version data for '{path}'.");
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
