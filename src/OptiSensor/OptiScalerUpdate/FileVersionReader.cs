using System.Diagnostics;

namespace OptiSensor.OptiScalerUpdate;

/// <summary>
/// The Win32 version-resource fields OptiSensor uses to recognise an OptiScaler proxy DLL and to
/// tell which OptiScaler version family it belongs to. Only what the updater needs - not a general
/// file-metadata model.
/// </summary>
internal sealed record OptiScalerFileVersion(
    int Major,
    int Minor,
    int Build,
    int Private,
    string? ProductName,
    string? InternalName,
    string? OriginalFilename,
    string? FileDescription,
    string? FileVersionText)
{
    public Version NumericVersion => new(Major, Minor, Math.Max(0, Build), Math.Max(0, Private));

    public bool HasReadableNumericVersion =>
        !string.IsNullOrWhiteSpace(FileVersionText) && Version.TryParse(FileVersionText, out _);

    /// <summary>True when any identity field carries the "OptiScaler" marker, matching how the
    /// OptiScaler build stamps its proxy DLL (ProductName / InternalName / OriginalFilename /
    /// FileDescription are all "OptiScaler" on a real 0.9 build).</summary>
    public bool LooksLikeOptiScaler =>
        new[] { ProductName, InternalName, OriginalFilename, FileDescription }
            .Any(identity => identity?.Contains("OptiScaler", StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>The OptiSensor Claw updater supports exactly one OptiScaler family: 0.9.x.</summary>
    public bool IsSupportedNineFamily => Major == 0 && Minor == 9;
}

internal interface IFileVersionReader
{
    OptiScalerFileVersion Read(string path);
}

internal sealed class SystemFileVersionReader : IFileVersionReader
{
    public OptiScalerFileVersion Read(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return new OptiScalerFileVersion(
            info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart,
            info.ProductName, info.InternalName, info.OriginalFilename, info.FileDescription, info.FileVersion);
    }
}
