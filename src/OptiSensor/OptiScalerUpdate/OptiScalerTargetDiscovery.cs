namespace OptiSensor.OptiScalerUpdate;

internal enum OptiScalerDiscoveryStatus
{
    /// <summary>Exactly one OptiScaler 0.9 proxy DLL was found directly in the folder.</summary>
    Found,

    /// <summary>The folder has no DLL that identifies itself as OptiScaler.</summary>
    NotFound,

    /// <summary>An OptiScaler proxy DLL was found, but its version is not the supported 0.9 family.</summary>
    UnsupportedVersion,

    /// <summary>More than one DLL in the folder identifies itself as OptiScaler - ambiguous.</summary>
    MultipleFound,

    /// <summary>The path is empty, missing, or not a readable directory.</summary>
    InvalidFolder,
}

/// <summary>
/// What <see cref="OptiScalerTargetDiscovery.Discover"/> resolved for a game folder. On
/// <see cref="OptiScalerDiscoveryStatus.Found"/>, <see cref="TargetDllPath"/> is the exact existing
/// proxy DLL the updater must replace in place (keeping its filename).
/// </summary>
internal sealed record OptiScalerDiscoveryResult(
    OptiScalerDiscoveryStatus Status,
    string? TargetDllPath,
    Version? Version,
    IReadOnlyList<string> DetectedPaths,
    string Message)
{
    public static OptiScalerDiscoveryResult Found(string path, Version version) =>
        new(OptiScalerDiscoveryStatus.Found, path, version, [path],
            $"OptiScaler {version} was found: {Path.GetFileName(path)}");

    public static OptiScalerDiscoveryResult NotFound() =>
        new(OptiScalerDiscoveryStatus.NotFound, null, null, [],
            "No OptiScaler DLL was found directly in the selected folder.");

    public static OptiScalerDiscoveryResult UnsupportedVersion(string path, Version? version) =>
        new(OptiScalerDiscoveryStatus.UnsupportedVersion, path, version, [path],
            version is null
                ? $"OptiScaler was found ({Path.GetFileName(path)}) but its version could not be read. Only OptiScaler 0.9 is supported."
                : $"OptiScaler {version} was found, but only OptiScaler 0.9 is supported.");

    public static OptiScalerDiscoveryResult MultipleFound(IReadOnlyList<string> paths) =>
        new(OptiScalerDiscoveryStatus.MultipleFound, null, null, paths,
            $"Multiple OptiScaler DLLs were detected: {string.Join(", ", paths.Select(Path.GetFileName))}. Remove the extra ones and try again.");

    public static OptiScalerDiscoveryResult InvalidFolder() =>
        new(OptiScalerDiscoveryStatus.InvalidFolder, null, null, [],
            "Select an existing game folder.");
}

/// <summary>
/// Resolves the existing OptiScaler proxy DLL inside a user-selected game folder so the future UI
/// can take a folder, not a specific DLL. Inspection is <b>top-level only</b> and by real Win32
/// version metadata via the shared <see cref="OptiScalerFileVersion.IsOptiScaler"/> identity rule -
/// never by filename. A DLL that cannot be inspected is skipped (discovery keeps going); this is the
/// opposite of the update path, where the already-chosen target failing inspection is a hard error.
/// </summary>
internal sealed class OptiScalerTargetDiscovery(IFileVersionReader versionReader)
{
    public OptiScalerDiscoveryResult Discover(string gameFolderPath)
    {
        if (string.IsNullOrWhiteSpace(gameFolderPath))
            return OptiScalerDiscoveryResult.InvalidFolder();

        string fullPath;
        try { fullPath = Path.GetFullPath(gameFolderPath); }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return OptiScalerDiscoveryResult.InvalidFolder();
        }

        if (!Directory.Exists(fullPath))
            return OptiScalerDiscoveryResult.InvalidFolder();

        List<string> dlls;
        try
        {
            dlls = Directory
                .EnumerateFiles(fullPath, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OptiScalerDiscoveryResult.InvalidFolder();
        }

        var candidates = new List<(string Path, OptiScalerFileVersion Version)>();
        foreach (var dll in dlls)
        {
            OptiScalerFileVersion version;
            try { version = versionReader.Read(dll); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FileNotFoundException)
            {
                continue; // unrelated / unreadable DLL - not an OptiScaler candidate, keep scanning
            }

            if (version.IsOptiScaler)
                candidates.Add((Path.GetFullPath(dll), version));
        }

        if (candidates.Count == 0)
            return OptiScalerDiscoveryResult.NotFound();
        if (candidates.Count > 1)
            return OptiScalerDiscoveryResult.MultipleFound(candidates.Select(c => c.Path).ToList());

        var (targetPath, target) = candidates[0];
        if (!target.HasReadableNumericVersion || !target.IsSupportedNineFamily)
            return OptiScalerDiscoveryResult.UnsupportedVersion(
                targetPath, target.HasReadableNumericVersion ? target.NumericVersion : null);

        return OptiScalerDiscoveryResult.Found(targetPath, target.NumericVersion);
    }
}
