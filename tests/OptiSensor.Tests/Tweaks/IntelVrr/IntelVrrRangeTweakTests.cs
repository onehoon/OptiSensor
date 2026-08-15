using OptiSensor.Tweaks.IntelVrr;
using Xunit;

namespace OptiSensor.Tests.Tweaks.IntelVrr;

public class IntelVrrRangeTweakTests
{
    private static readonly PanelIdentity AffectedPanel = new("CSW", "0801", "PN8007QB1-2");
    private static readonly PanelIdentity OtherPanel = new("AUO", "1234", "Some Other Panel");

    private static IntelDisplayOutputHandle MakeOutput(nint handle) => new(1, handle, null);

    private static IntelVrrRangeTweak CreateTweak(FakeIntelArcSyncClient client, IReadOnlyList<PanelIdentity> panels)
    {
        return new IntelVrrRangeTweak(() => client, () => panels);
    }

    [Fact]
    public void Run_ToggleDisabled_DoesNothingAndReportsDisabled()
    {
        var client = new FakeIntelArcSyncClient();
        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: false);

        Assert.Equal(IntelVrrRunStatus.Disabled, result.Status);
        Assert.Equal(0, client.SetCallCount);
    }

    [Fact]
    public void Run_UnsupportedPanel_DoesNothing()
    {
        var client = new FakeIntelArcSyncClient();
        var tweak = CreateTweak(client, [OtherPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.UnsupportedPanel, result.Status);
        Assert.Equal(0, client.SetCallCount);
    }

    [Fact]
    public void Run_NoPanelsDetected_ReportsUnsupportedPanel()
    {
        var client = new FakeIntelArcSyncClient();
        var tweak = CreateTweak(client, []);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.UnsupportedPanel, result.Status);
    }

    [Fact]
    public void Run_IgclUnavailable_ReportsUnavailableAndDoesNotMutate()
    {
        var client = new FakeIntelArcSyncClient { InitializeResult = false };
        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.Unavailable, result.Status);
        Assert.Equal(0, client.SetCallCount);
    }

    [Fact]
    public void Run_AlreadyExcellentWithNativeRange_ReportsAlreadyCorrect_NoSetCall()
    {
        var client = new FakeIntelArcSyncClient();
        var output = MakeOutput(10);
        client.Outputs.Add(output);
        client.InfoByOutput[output.DisplayOutputHandle] = new IntelArcSyncInfo(
            IsArcSyncSupported: true,
            CapabilityMinRefreshHz: 48,
            CapabilityMaxRefreshHz: 120,
            CurrentProfile: CtlIntelArcSyncProfile.Excellent,
            CurrentMinRefreshHz: 48,
            CurrentMaxRefreshHz: 120);

        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.AlreadyCorrect, result.Status);
        Assert.Equal(0, client.SetCallCount);
    }

    [Fact]
    public void Run_ConstrainedOrdinaryProfile_SetsExcellentAndVerifiesApplied()
    {
        var client = new FakeIntelArcSyncClient();
        var output = MakeOutput(10);
        client.Outputs.Add(output);
        client.InfoByOutput[output.DisplayOutputHandle] = new IntelArcSyncInfo(
            IsArcSyncSupported: true,
            CapabilityMinRefreshHz: 48,
            CapabilityMaxRefreshHz: 120,
            CurrentProfile: CtlIntelArcSyncProfile.Default,
            CurrentMinRefreshHz: 60,
            CurrentMaxRefreshHz: 120);
        client.InfoAfterSet = new IntelArcSyncInfo(true, 48, 120, CtlIntelArcSyncProfile.Excellent, 48, 120);

        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.Applied, result.Status);
        Assert.Equal(1, client.SetCallCount);
        Assert.Equal(CtlIntelArcSyncProfile.Excellent, client.LastSetProfile);
        Assert.Contains("60", result.RangeBeforeText);
        Assert.Contains("48", result.RangeAfterText);
    }

    [Fact]
    public void Run_SetCallFails_ReportsApplyFailedWithoutFalseSuccess()
    {
        var client = new FakeIntelArcSyncClient { SetShouldSucceed = false, SetErrorMessage = "driver rejected" };
        var output = MakeOutput(10);
        client.Outputs.Add(output);
        client.InfoByOutput[output.DisplayOutputHandle] = new IntelArcSyncInfo(
            true, 48, 120, CtlIntelArcSyncProfile.Default, 60, 120);

        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.ApplyFailed, result.Status);
        Assert.Contains("driver rejected", result.Message);
    }

    [Fact]
    public void Run_SetSucceedsButReadbackWrong_ReportsVerificationFailed()
    {
        var client = new FakeIntelArcSyncClient();
        var output = MakeOutput(10);
        client.Outputs.Add(output);
        client.InfoByOutput[output.DisplayOutputHandle] = new IntelArcSyncInfo(
            true, 48, 120, CtlIntelArcSyncProfile.Default, 60, 120);
        // Readback after SET still shows the constrained range - verification should fail.
        client.InfoAfterSet = new IntelArcSyncInfo(true, 48, 120, CtlIntelArcSyncProfile.Excellent, 60, 120);

        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.VerificationFailed, result.Status);
        Assert.Equal(1, client.SetCallCount);
    }

    [Fact]
    public void Run_CustomProfile_IsPreserved_NoSetCall() => AssertUserManagedProfilePreserved(CtlIntelArcSyncProfile.Custom);

    [Fact]
    public void Run_OffProfile_IsPreserved_NoSetCall() => AssertUserManagedProfilePreserved(CtlIntelArcSyncProfile.Off);

    private static void AssertUserManagedProfilePreserved(CtlIntelArcSyncProfile profile)
    {
        var client = new FakeIntelArcSyncClient();
        var output = MakeOutput(10);
        client.Outputs.Add(output);
        client.InfoByOutput[output.DisplayOutputHandle] = new IntelArcSyncInfo(
            true, 48, 120, profile, 60, 120);

        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.SkippedUserProfile, result.Status);
        Assert.Equal(0, client.SetCallCount);
    }

    [Fact]
    public void Run_AmbiguousDisplays_NoSetCall_ReportsAmbiguous()
    {
        var client = new FakeIntelArcSyncClient();
        var outputA = MakeOutput(10);
        var outputB = MakeOutput(20);
        client.Outputs.Add(outputA);
        client.Outputs.Add(outputB);
        // Neither is a clean native-range single candidate - both differ from 48-120.
        client.InfoByOutput[outputA.DisplayOutputHandle] = new IntelArcSyncInfo(true, 60, 165, CtlIntelArcSyncProfile.Default, 60, 165);
        client.InfoByOutput[outputB.DisplayOutputHandle] = new IntelArcSyncInfo(true, 40, 144, CtlIntelArcSyncProfile.Default, 40, 144);

        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.AmbiguousDisplay, result.Status);
        Assert.Equal(0, client.SetCallCount);
    }

    [Fact]
    public void Run_AmbiguousDisplays_SingleNativeRangeCandidate_IsUsedAsFallback()
    {
        var client = new FakeIntelArcSyncClient();
        var outputA = MakeOutput(10);
        var outputB = MakeOutput(20);
        client.Outputs.Add(outputA);
        client.Outputs.Add(outputB);
        client.InfoByOutput[outputA.DisplayOutputHandle] = new IntelArcSyncInfo(true, 48, 120, CtlIntelArcSyncProfile.Default, 60, 120);
        client.InfoByOutput[outputB.DisplayOutputHandle] = new IntelArcSyncInfo(true, 40, 144, CtlIntelArcSyncProfile.Default, 40, 144);
        client.InfoAfterSet = new IntelArcSyncInfo(true, 48, 120, CtlIntelArcSyncProfile.Excellent, 48, 120);

        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.Applied, result.Status);
        Assert.Equal(1, client.SetCallCount);
    }
}
