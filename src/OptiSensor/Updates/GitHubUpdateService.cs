using Velopack;
using Velopack.Sources;

namespace OptiSensor.Updates;

internal static class GitHubUpdateService
{
    private const string RepositoryUrl = "https://github.com/onehoon/OptiSensor";

    public static async Task<PreparedUpdateResult> DownloadLatestAsync(
        Action<string>? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manager = new UpdateManager(new GithubSource(RepositoryUrl, null, prerelease: false));
        if (!manager.IsInstalled)
            return PreparedUpdateResult.NotInstalled();

        reportProgress?.Invoke("Checking GitHub Releases...");
        var update = await manager.CheckForUpdatesAsync()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (update is null)
            return PreparedUpdateResult.UpToDate();

        var version = update.TargetFullRelease.Version.ToString();
        reportProgress?.Invoke($"Downloading version {version}...");
        await manager.DownloadUpdatesAsync(
            update,
            percent =>
            {
                if (!cancellationToken.IsCancellationRequested)
                    reportProgress?.Invoke($"Downloading version {version}: {percent}%");
            },
            cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return PreparedUpdateResult.Ready(manager, update.TargetFullRelease, version);
    }

    public static void ApplyAndRestart(PreparedUpdateResult result, string[]? restartArgs = null)
    {
        if (result.Manager is null || result.Asset is null)
            throw new InvalidOperationException("No downloaded update is available to apply.");

        result.Manager.ApplyUpdatesAndRestart(result.Asset, restartArgs);
    }
}

internal sealed record PreparedUpdateResult(
    UpdateManager? Manager,
    VelopackAsset? Asset,
    string Message,
    bool IsReady)
{
    public static PreparedUpdateResult NotInstalled() =>
        new(null, null, "Updates are available after installing OptiSensor with the Velopack Setup.exe.", false);

    public static PreparedUpdateResult UpToDate() =>
        new(null, null, "OptiSensor is already up to date.", false);

    public static PreparedUpdateResult Ready(UpdateManager manager, VelopackAsset asset, string version) =>
        new(manager, asset, $"Version {version} is ready. Restart OptiSensor to apply it.", true);
}
