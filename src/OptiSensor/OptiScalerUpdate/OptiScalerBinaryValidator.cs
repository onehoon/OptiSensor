using System.Security.Cryptography;

namespace OptiSensor.OptiScalerUpdate;

/// <summary>Why an OptiScaler DLL (downloaded source or existing target) failed validation.</summary>
internal enum OptiScalerBinaryProblem
{
    None,
    Missing,
    Unreadable,
    NotOptiScaler,
    NoReadableVersion,
    UnsupportedVersion,
}

internal sealed record OptiScalerBinaryInfo(string Path, Version Version, long Length);

internal sealed record OptiScalerBinaryValidation(OptiScalerBinaryInfo? Binary, OptiScalerBinaryProblem Problem, string? Error)
{
    public bool IsValid => Binary is not null;

    public static OptiScalerBinaryValidation Ok(OptiScalerBinaryInfo binary) => new(binary, OptiScalerBinaryProblem.None, null);
    public static OptiScalerBinaryValidation Fail(OptiScalerBinaryProblem problem, string error) => new(null, problem, error);
}

/// <summary>
/// Validates that a file on disk is a usable OptiScaler 0.9 proxy DLL: it exists, is a non-empty
/// readable file, its Win32 version resource identifies it as OptiScaler, it carries a readable
/// numeric version, and that version is in the supported 0.9 family. Adapted from OptiEditor's
/// <c>OptiScalerSourceValidator</c>, narrowed to the single family OptiSensor Claw supports.
/// </summary>
internal sealed class OptiScalerBinaryValidator(IFileVersionReader versionReader)
{
    public OptiScalerBinaryValidation Validate(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var file = new FileInfo(fullPath);
            if (!file.Exists)
                return OptiScalerBinaryValidation.Fail(OptiScalerBinaryProblem.Missing, "The OptiScaler DLL was not found.");
            if (file.Length == 0)
                return OptiScalerBinaryValidation.Fail(OptiScalerBinaryProblem.Unreadable, "The OptiScaler DLL is empty.");

            // Confirm the file is at least readable right now (a locked file surfaces here).
            using (File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read)) { }

            var version = versionReader.Read(fullPath);
            if (!version.IsOptiScaler)
                return OptiScalerBinaryValidation.Fail(OptiScalerBinaryProblem.NotOptiScaler,
                    "The file is not an OptiScaler binary.");
            if (!version.HasReadableNumericVersion)
                return OptiScalerBinaryValidation.Fail(OptiScalerBinaryProblem.NoReadableVersion,
                    "The OptiScaler DLL has no readable version information.");
            if (!version.IsSupportedNineFamily)
                return OptiScalerBinaryValidation.Fail(OptiScalerBinaryProblem.UnsupportedVersion,
                    $"OptiScaler {version.NumericVersion} is not supported. Only OptiScaler 0.9 can be updated here.");

            return OptiScalerBinaryValidation.Ok(new OptiScalerBinaryInfo(fullPath, version.NumericVersion, file.Length));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return OptiScalerBinaryValidation.Fail(OptiScalerBinaryProblem.Unreadable,
                "The OptiScaler DLL could not be read.");
        }
    }

    public static async Task<byte[]> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
    }
}
