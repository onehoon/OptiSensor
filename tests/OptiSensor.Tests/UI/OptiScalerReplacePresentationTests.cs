using OptiSensor.OptiScalerUpdate;
using OptiSensor.UI;
using Xunit;

namespace OptiSensor.Tests.UI;

/// <summary>
/// The one piece of the OptiScaler Replace dialog worth unit-testing: which discovery status lets
/// the user press Replace, and how core results become user messages. Everything else is thin WPF
/// code-behind over the already-covered updater core (#68) and folder discovery (#69).
/// </summary>
public sealed class OptiScalerReplacePresentationTests
{
    private static OptiScalerDiscoveryResult Found() =>
        OptiScalerDiscoveryResult.Found(@"C:\Games\Example\dxgi.dll", new Version(0, 9, 5, 3));

    [Fact]
    public void Replace_is_enabled_only_for_a_found_target()
    {
        Assert.True(OptiScalerReplacePresentation.CanReplace(Found(), busy: false));
    }

    [Theory]
    [InlineData("notfound")]
    [InlineData("unsupported")]
    [InlineData("multiple")]
    [InlineData("invalid")]
    public void Replace_is_disabled_for_every_non_found_status(string kind)
    {
        var discovery = kind switch
        {
            "notfound" => OptiScalerDiscoveryResult.NotFound(),
            "unsupported" => OptiScalerDiscoveryResult.UnsupportedVersion(@"C:\g\dxgi.dll", new Version(0, 10, 0, 0)),
            "multiple" => OptiScalerDiscoveryResult.MultipleFound([@"C:\g\a.dll", @"C:\g\b.dll"]),
            _ => OptiScalerDiscoveryResult.InvalidFolder(),
        };

        Assert.NotEqual(OptiScalerDiscoveryStatus.Found, discovery.Status);
        Assert.False(OptiScalerReplacePresentation.CanReplace(discovery, busy: false));
    }

    [Fact]
    public void Replace_is_disabled_before_a_folder_is_picked()
    {
        Assert.False(OptiScalerReplacePresentation.CanReplace(null, busy: false));
    }

    [Fact]
    public void Replace_is_disabled_while_a_replacement_is_running()
    {
        Assert.False(OptiScalerReplacePresentation.CanReplace(Found(), busy: true));
    }

    [Fact]
    public void Found_shows_the_existing_proxy_filename_and_version()
    {
        var text = OptiScalerReplacePresentation.DescribeDiscovery(Found());
        Assert.Contains("dxgi.dll", text);
        Assert.Contains("0.9.5.3", text);
    }

    [Fact]
    public void Unsupported_version_is_not_reported_as_not_found()
    {
        var text = OptiScalerReplacePresentation.DescribeDiscovery(
            OptiScalerDiscoveryResult.UnsupportedVersion(@"C:\g\dxgi.dll", new Version(0, 10, 2, 0)));
        Assert.Contains("only OptiScaler 0.9 is supported", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not found", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void File_in_use_maps_to_a_close_the_game_message()
    {
        var text = OptiScalerReplacePresentation.DescribeResult(
            OptiScalerUpdateResult.Failed(OptiScalerUpdateReason.FileInUse, "raw"));
        Assert.Contains("Close the game", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("download")]
    [InlineData("archive")]
    [InlineData("source")]
    public void Download_and_source_failures_map_to_one_generic_message(string kind)
    {
        var reason = kind switch
        {
            "download" => OptiScalerUpdateReason.DownloadFailed,
            "archive" => OptiScalerUpdateReason.InvalidArchive,
            _ => OptiScalerUpdateReason.SourceValidationFailed,
        };
        var text = OptiScalerReplacePresentation.DescribeResult(OptiScalerUpdateResult.Failed(reason, "raw"));
        Assert.Contains("could not be downloaded or validated", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Skipped_is_shown_as_up_to_date_not_as_an_error()
    {
        var text = OptiScalerReplacePresentation.DescribeResult(OptiScalerUpdateResult.Skipped());
        Assert.Contains("already up to date", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Replaced_is_shown_as_success()
    {
        var text = OptiScalerReplacePresentation.DescribeResult(OptiScalerUpdateResult.Replaced("0.9.5.3"));
        Assert.Contains("replaced successfully", text, StringComparison.OrdinalIgnoreCase);
    }
}
