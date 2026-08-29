using System.IO.Compression;
using System.Net.Http;

namespace OptiSensor.OptiScalerUpdate;

internal sealed record OptiScalerDownloadResult(
    string? StagingDirectory,
    string? DllPath,
    OptiScalerUpdateReason FailureReason,
    string? Error)
{
    public bool Succeeded => DllPath is not null;

    public static OptiScalerDownloadResult Ok(string stagingDirectory, string dllPath) =>
        new(stagingDirectory, dllPath, OptiScalerUpdateReason.None, null);

    public static OptiScalerDownloadResult Fail(OptiScalerUpdateReason reason, string error) =>
        new(null, null, reason, error);
}

/// <summary>
/// Fetches the fixed rolling-release asset <c>OptiScaler-0.9.zip</c> (tag
/// <c>optiscaler-sensor-latest</c>), treats it as untrusted input, and extracts its root
/// <c>OptiScaler.dll</c> into a fresh staging directory. It never touches the normal OptiSensor
/// application release, and it only runs when <see cref="OptiScalerUpdateService.UpdateAsync"/>
/// calls it - there is no polling or pre-download cache.
/// </summary>
internal sealed class OptiScalerReleaseDownloader
{
    private const string AssetUrl =
        "https://github.com/onehoon/OptiSensor/releases/download/optiscaler-sensor-latest/OptiScaler-0.9.zip";

    private const string ExpectedEntryName = "OptiScaler.dll";
    private const long MaxArchiveBytes = 256L * 1024 * 1024;
    private const long MaxExtractedBytes = 256L * 1024 * 1024;

    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    private readonly HttpClient _httpClient;

    public OptiScalerReleaseDownloader(HttpClient? httpClient = null) => _httpClient = httpClient ?? SharedClient;

    public async Task<OptiScalerDownloadResult> DownloadAsync(CancellationToken cancellationToken)
    {
        var stagingDirectory = Path.Combine(
            Path.GetTempPath(), "optisensor-optiscaler-update", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            var zipPath = Path.Combine(stagingDirectory, "OptiScaler-0.9.zip");

            try
            {
                using var response = await _httpClient
                    .GetAsync(AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return Cleanup(stagingDirectory, OptiScalerDownloadResult.Fail(
                        OptiScalerUpdateReason.DownloadFailed,
                        $"Downloading OptiScaler-0.9.zip failed (HTTP {(int)response.StatusCode})."));

                if (response.Content.Headers.ContentLength is { } declared && declared > MaxArchiveBytes)
                    return Cleanup(stagingDirectory, OptiScalerDownloadResult.Fail(
                        OptiScalerUpdateReason.InvalidArchive, "The downloaded archive is unexpectedly large."));

                await using (var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var fileStream = File.Create(zipPath))
                {
                    await httpStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (HttpRequestException ex)
            {
                return Cleanup(stagingDirectory, OptiScalerDownloadResult.Fail(
                    OptiScalerUpdateReason.DownloadFailed, $"Downloading OptiScaler-0.9.zip failed: {ex.Message}"));
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Cleanup(stagingDirectory, OptiScalerDownloadResult.Fail(
                    OptiScalerUpdateReason.DownloadFailed, "Downloading OptiScaler-0.9.zip timed out."));
            }

            if (new FileInfo(zipPath).Length == 0)
                return Cleanup(stagingDirectory, OptiScalerDownloadResult.Fail(
                    OptiScalerUpdateReason.InvalidArchive, "The downloaded archive is empty."));

            var dllPath = Path.Combine(stagingDirectory, ExpectedEntryName);
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.GetEntry(ExpectedEntryName);
                if (entry is null || !string.Equals(entry.FullName, ExpectedEntryName, StringComparison.Ordinal))
                    return Cleanup(stagingDirectory, OptiScalerDownloadResult.Fail(
                        OptiScalerUpdateReason.InvalidArchive,
                        "The archive does not contain OptiScaler.dll at its root."));
                if (entry.Length is 0 or > MaxExtractedBytes)
                    return Cleanup(stagingDirectory, OptiScalerDownloadResult.Fail(
                        OptiScalerUpdateReason.InvalidArchive, "The archived OptiScaler.dll has an unexpected size."));

                entry.ExtractToFile(dllPath, overwrite: true);
            }
            catch (InvalidDataException)
            {
                return Cleanup(stagingDirectory, OptiScalerDownloadResult.Fail(
                    OptiScalerUpdateReason.InvalidArchive, "The downloaded file is not a valid ZIP archive."));
            }

            if (!File.Exists(dllPath) || new FileInfo(dllPath).Length == 0)
                return Cleanup(stagingDirectory, OptiScalerDownloadResult.Fail(
                    OptiScalerUpdateReason.InvalidArchive, "OptiScaler.dll could not be extracted."));

            try { File.Delete(zipPath); } catch (IOException) { }

            return OptiScalerDownloadResult.Ok(stagingDirectory, dllPath);
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(stagingDirectory);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Cleanup(stagingDirectory, OptiScalerDownloadResult.Fail(
                OptiScalerUpdateReason.DownloadFailed, $"Preparing the OptiScaler download failed: {ex.Message}"));
        }
    }

    public static void TryDeleteDirectory(string? directory)
    {
        if (directory is null) return;
        try { Directory.Delete(directory, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static OptiScalerDownloadResult Cleanup(string directory, OptiScalerDownloadResult result)
    {
        TryDeleteDirectory(directory);
        return result;
    }
}
