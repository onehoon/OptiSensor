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

    /// <summary>
    /// The single OptiScaler identity rule shared by folder discovery and update/replacement
    /// validation: the Win32 <c>ProductName</c> or <c>FileDescription</c> is <em>exactly</em>
    /// "OptiScaler" (case-insensitive, trimmed). A real OptiScaler build stamps both. Filename,
    /// <c>CompanyName</c>, <c>InternalName</c> and <c>OriginalFilename</c> are deliberately not
    /// identity - and substring matches ("MyOptiScaler", "OptiScaler Helper") are rejected.
    /// </summary>
    public bool IsOptiScaler => IsExactlyOptiScaler(ProductName) || IsExactlyOptiScaler(FileDescription);

    private static bool IsExactlyOptiScaler(string? value) =>
        string.Equals(value?.Trim(), "OptiScaler", StringComparison.OrdinalIgnoreCase);

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
