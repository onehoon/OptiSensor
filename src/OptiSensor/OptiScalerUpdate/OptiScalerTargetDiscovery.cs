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
/// can take a folder, not a specific DLL. Some games install OptiScaler in a subfolder
/// (<c>Binaries\Win64</c>, <c>bin</c>, <c>x64</c>), so this walks the selected tree
/// <b>breadth-first</b> - the selected folder, then its immediate children, then their children -
/// and returns the <b>first supported OptiScaler 0.9 target it reaches</b>, stopping immediately
/// (a target closer to the game root wins over a deeper one).
///
/// A candidate must be both a supported load/proxy filename (<see cref="SupportedTargetFileNames"/>,
/// case-insensitive) <b>and</b> carry OptiScaler identity via the shared
/// <see cref="OptiScalerFileVersion.IsOptiScaler"/> rule - the filename is checked first, so backup
/// copies that keep the metadata (<c>dxgi_backup.dll</c>, <c>OptiScaler-old.dll</c>) are ignored
/// without even reading their version resource. Per directory: one candidate that is 0.9.x ->
/// <see cref="OptiScalerDiscoveryStatus.Found"/>; more than one candidate ->
/// <see cref="OptiScalerDiscoveryStatus.MultipleFound"/>. An unsupported
/// (e.g. 0.10) OptiScaler seen along the way is only reported
/// (<see cref="OptiScalerDiscoveryStatus.UnsupportedVersion"/>) if the whole walk finds no 0.9
/// target. Unreadable DLLs and inaccessible subdirectories are skipped; reparse points (junctions /
/// symlinks) are not followed. Only a root that cannot be traversed at all yields
/// <see cref="OptiScalerDiscoveryStatus.InvalidFolder"/>.
/// </summary>
internal sealed class OptiScalerTargetDiscovery(IFileVersionReader versionReader)
{
    public OptiScalerDiscoveryResult Discover(string gameFolderPath)
    {
        if (string.IsNullOrWhiteSpace(gameFolderPath))
            return OptiScalerDiscoveryResult.InvalidFolder();

        string root;
        try { root = Path.GetFullPath(gameFolderPath); }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return OptiScalerDiscoveryResult.InvalidFolder();
        }

        if (!Directory.Exists(root))
            return OptiScalerDiscoveryResult.InvalidFolder();

        // Plain FIFO queue = breadth-first: a directory's children are enqueued behind every
        // still-pending directory of the current depth, so each depth is fully searched first.
        var queue = new Queue<string>();
        queue.Enqueue(root);
        (string Path, Version? Version)? firstUnsupported = null;

        while (queue.Count > 0)
        {
            var directory = queue.Dequeue();

            List<(string Path, OptiScalerFileVersion Version)> matches;
            try
            {
                matches = FindOptiScalerDlls(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A subdirectory we can't read is skipped; if the selected root itself can't be
                // read, the walk never really starts.
                if (PathsEqual(directory, root))
                    return OptiScalerDiscoveryResult.InvalidFolder();
                continue;
            }

            if (matches.Count > 1)
                return OptiScalerDiscoveryResult.MultipleFound(matches.Select(m => m.Path).ToList());

            if (matches.Count == 1)
            {
                var (path, version) = matches[0];
                if (version.HasReadableNumericVersion && version.IsSupportedNineFamily)
                    return OptiScalerDiscoveryResult.Found(path, version.NumericVersion);

                firstUnsupported ??= (path, version.HasReadableNumericVersion ? version.NumericVersion : null);
            }

            EnqueueChildDirectories(directory, queue);
        }

        return firstUnsupported is { } unsupported
            ? OptiScalerDiscoveryResult.UnsupportedVersion(unsupported.Path, unsupported.Version)
            : OptiScalerDiscoveryResult.NotFound();
    }

    // The only filenames a game will actually load OptiScaler as. A backup copy
    // (dxgi_backup.dll, OptiScaler-old.dll, ...) keeps the OptiScaler PE metadata but is never
    // loaded, so it must not be a replacement target, must not create a false MultipleFound, and
    // must not affect version-family detection. Filename is checked *before* reading any metadata.
    private static readonly HashSet<string> SupportedTargetFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dxgi.dll",
        "winmm.dll",
        "d3d12.dll",
        "dbghelp.dll",
        "version.dll",
        "wininet.dll",
        "winhttp.dll",
        "OptiScaler.dll",
        "OptiScaler.asi",
    };

    private List<(string Path, OptiScalerFileVersion Version)> FindOptiScalerDlls(string directory)
    {
        var matches = new List<(string, OptiScalerFileVersion)>();
        foreach (var file in Directory
                     .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => SupportedTargetFileNames.Contains(Path.GetFileName(path)))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            OptiScalerFileVersion version;
            try { version = versionReader.Read(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FileNotFoundException)
            {
                continue; // supported name but unreadable - keep scanning this directory
            }

            if (version.IsOptiScaler)
                matches.Add((Path.GetFullPath(file), version));
        }
        return matches;
    }

    private static void EnqueueChildDirectories(string directory, Queue<string> queue)
    {
        List<string> children;
        try
        {
            children = Directory
                .EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var child in children)
        {
            try
            {
                // Never follow junctions / symlinks: they can loop or point outside the selected
                // game-folder tree. Skipping them removes the need for a visited-path set.
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
            {
                continue;
            }

            queue.Enqueue(child);
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(a),
            Path.TrimEndingDirectorySeparator(b),
            StringComparison.OrdinalIgnoreCase);
}
