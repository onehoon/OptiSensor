using System.Net.Http;
using OptiSensor.App;

namespace OptiSensor.OptiScalerUpdate;

/// <summary>
/// The single application-facing authority for one OptiScaler update attempt: download the latest
/// patched OptiScaler 0.9 build from the fixed rolling release, validate it, and replace one
/// caller-selected existing OptiScaler proxy DLL in place. The future UI provides only a target path
/// and a <see cref="CancellationToken"/> and receives one <see cref="OptiScalerUpdateResult"/>; it
/// does not need to know about the download / staging / replacement steps.
/// </summary>
internal sealed class OptiScalerUpdateService
{
    private readonly OptiScalerBinaryValidator _validator;
    private readonly OptiScalerReleaseDownloader _downloader;
    private readonly OptiScalerReplacementService _replacement;

    public OptiScalerUpdateService()
        : this(new SystemFileVersionReader(), new OptiScalerReleaseDownloader())
    {
    }

    internal OptiScalerUpdateService(IFileVersionReader versionReader, HttpClient httpClient)
        : this(versionReader, new OptiScalerReleaseDownloader(httpClient))
    {
    }

    private OptiScalerUpdateService(IFileVersionReader versionReader, OptiScalerReleaseDownloader downloader)
    {
        _validator = new OptiScalerBinaryValidator(versionReader);
        _downloader = downloader;
        _replacement = new OptiScalerReplacementService(versionReader, _validator);
    }

    public async Task<OptiScalerUpdateResult> UpdateAsync(string targetDllPath, CancellationToken cancellationToken)
    {
        SimpleLog.TryWrite("OptiScaler update started.");
        try
        {
            if (string.IsNullOrWhiteSpace(targetDllPath) || !File.Exists(targetDllPath))
                return Log(OptiScalerUpdateResult.Failed(OptiScalerUpdateReason.TargetMissing,
                    "Select an existing OptiScaler DLL first."));

            var targetCheck = _validator.Validate(targetDllPath);
            if (!targetCheck.IsValid)
                return Log(OptiScalerUpdateResult.Failed(TargetReason(targetCheck.Problem), targetCheck.Error!));
            SimpleLog.TryWrite($"OptiScaler update: target validated ({targetCheck.Binary!.Version}).");

            cancellationToken.ThrowIfCancellationRequested();

            var download = await _downloader.DownloadAsync(cancellationToken).ConfigureAwait(false);
            if (!download.Succeeded)
                return Log(OptiScalerUpdateResult.Failed(download.FailureReason, download.Error!));
            SimpleLog.TryWrite("OptiScaler update: patched build downloaded.");

            try
            {
                var sourceCheck = _validator.Validate(download.DllPath!);
                if (!sourceCheck.IsValid)
                    return Log(OptiScalerUpdateResult.Failed(OptiScalerUpdateReason.SourceValidationFailed,
                        $"The downloaded OptiScaler build failed validation: {sourceCheck.Error}"));

                var sourceHash = await OptiScalerBinaryValidator
                    .Sha256Async(download.DllPath!, cancellationToken)
                    .ConfigureAwait(false);
                SimpleLog.TryWrite($"OptiScaler update: source validated ({sourceCheck.Binary!.Version}).");

                var result = await _replacement
                    .ReplaceAsync(download.DllPath!, sourceHash, targetDllPath, cancellationToken)
                    .ConfigureAwait(false);
                return Log(result);
            }
            finally
            {
                OptiScalerReleaseDownloader.TryDeleteDirectory(download.StagingDirectory);
            }
        }
        catch (OperationCanceledException)
        {
            return Log(OptiScalerUpdateResult.Canceled());
        }
        catch (Exception ex)
        {
            return Log(OptiScalerUpdateResult.Failed(OptiScalerUpdateReason.UnexpectedFailure,
                "The OptiScaler update did not complete.", ex));
        }
    }

    private static OptiScalerUpdateReason TargetReason(OptiScalerBinaryProblem problem) => problem switch
    {
        OptiScalerBinaryProblem.Missing => OptiScalerUpdateReason.TargetMissing,
        OptiScalerBinaryProblem.UnsupportedVersion => OptiScalerUpdateReason.UnsupportedTargetVersion,
        OptiScalerBinaryProblem.NotOptiScaler or OptiScalerBinaryProblem.NoReadableVersion => OptiScalerUpdateReason.TargetNotOptiScaler,
        _ => OptiScalerUpdateReason.TargetNotOptiScaler,
    };

    private static OptiScalerUpdateResult Log(OptiScalerUpdateResult result)
    {
        if (result.Exception is not null)
            SimpleLog.TryWriteException(result.Exception);
        SimpleLog.TryWrite($"OptiScaler update {result.Status} ({result.Reason}): {result.Message}");
        return result;
    }
}
