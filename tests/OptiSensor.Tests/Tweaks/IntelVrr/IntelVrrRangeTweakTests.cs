using OptiSensor.Tweaks.IntelVrr;
using Xunit;

namespace OptiSensor.Tests.Tweaks.IntelVrr;

public class IntelVrrRangeTweakTests
{
    private static readonly PanelIdentity AffectedPanel = new("CSW", "0801", "PN8007QB1-2", Active: true);
    private static readonly PanelIdentity OtherPanel = new("AUO", "1234", "Some Other Panel");

    private static IntelDisplayOutputHandle MakeOutput(nint handle, string? friendlyName = null) => new(1, handle, friendlyName);

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
    public void Run_PanelIdentityLookupThrows_ReportsUnavailableNotUnsupportedPanel_AndDoesNotMutate()
    {
        // A WMI enumeration failure (COM error, provider unavailable, access denied, etc.) must be
        // distinguishable from a genuine "WMI worked, no affected panel present" result - both by
        // status and in the log - rather than being silently collapsed into UnsupportedPanel.
        var client = new FakeIntelArcSyncClient();
        var tweak = new IntelVrrRangeTweak(() => client, () => throw new InvalidOperationException("WMI provider unavailable"));

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.Unavailable, result.Status);
        Assert.NotEqual(IntelVrrRunStatus.UnsupportedPanel, result.Status);
        Assert.Equal(0, client.SetCallCount);
        Assert.Contains(tweak.LastRunLog, line => line.Contains("Panel identity lookup failed") && line.Contains("InvalidOperationException"));
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
        client.CapabilityByOutput[output.DisplayOutputHandle] = new IntelArcSyncMonitorCapability(
            IsIntelArcSyncSupported: true,
            MinimumRefreshRateInHz: 48,
            MaximumRefreshRateInHz: 120,
            MaxFrameTimeIncreaseInUs: 0,
            MaxFrameTimeDecreaseInUs: 0);
        client.ProfileByOutput[output.DisplayOutputHandle] = new IntelArcSyncProfileState(
            Profile: CtlIntelArcSyncProfile.Excellent,
            MinRefreshRateInHz: 48,
            MaxRefreshRateInHz: 120,
            MaxFrameTimeIncreaseInUs: 0,
            MaxFrameTimeDecreaseInUs: 0);

        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.AlreadyCorrect, result.Status);
        Assert.Equal(0, client.SetCallCount);
    }

    [Fact]
    public void Run_RecommendedProfileAlreadyReportsNativeRange_ReportsAlreadyCorrect_NoSetCall()
    {
        var client = new FakeIntelArcSyncClient();
        var output = MakeOutput(10);
        client.Outputs.Add(output);
        client.CapabilityByOutput[output.DisplayOutputHandle] = new IntelArcSyncMonitorCapability(true, 48, 120, 0, 0);
        // Driver-managed profile (not literally EXCELLENT) but its active range already matches the
        // monitor's native capability range within tolerance - nothing to fix.
        client.ProfileByOutput[output.DisplayOutputHandle] = new IntelArcSyncProfileState(
            CtlIntelArcSyncProfile.Recommended, 48, 120, 0, 0);

        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.AlreadyCorrect, result.Status);
        Assert.Equal(0, client.SetCallCount);
    }

    [Fact]
    public void Run_RecommendedProfile_SetsExcellentAndVerifiesApplied() =>
        AssertOrdinaryDriverManagedProfileEligible(CtlIntelArcSyncProfile.Recommended);

    [Fact]
    public void Run_GoodProfile_SetsExcellentAndVerifiesApplied() =>
        AssertOrdinaryDriverManagedProfileEligible(CtlIntelArcSyncProfile.Good);

    [Fact]
    public void Run_CompatibleProfile_SetsExcellentAndVerifiesApplied() =>
        AssertOrdinaryDriverManagedProfileEligible(CtlIntelArcSyncProfile.Compatible);

    [Fact]
    public void Run_VesaProfile_SetsExcellentAndVerifiesApplied() =>
        AssertOrdinaryDriverManagedProfileEligible(CtlIntelArcSyncProfile.Vesa);

    private static void AssertOrdinaryDriverManagedProfileEligible(CtlIntelArcSyncProfile profile)
    {
        var client = new FakeIntelArcSyncClient();
        var output = MakeOutput(10);
        client.Outputs.Add(output);
        client.CapabilityByOutput[output.DisplayOutputHandle] = new IntelArcSyncMonitorCapability(true, 48, 120, 0, 0);
        client.ProfileByOutput[output.DisplayOutputHandle] = new IntelArcSyncProfileState(profile, 60, 120, 0, 0);
        client.ProfileAfterSet = new IntelArcSyncProfileState(CtlIntelArcSyncProfile.Excellent, 48, 120, 0, 0);

        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.Applied, result.Status);
        Assert.Equal(1, client.SetCallCount);
        Assert.Equal(CtlIntelArcSyncProfile.Excellent, client.LastSetProfile);
        Assert.Contains("60", result.RangeBeforeText);
        Assert.Contains("48", result.RangeAfterText);
    }

    [Fact]
    public void Run_LogsNativeCallResultCodesForEveryAttemptedCall()
    {
        var client = new FakeIntelArcSyncClient();
        var output = MakeOutput(10);
        client.Outputs.Add(output);
        client.CapabilityByOutput[output.DisplayOutputHandle] = new IntelArcSyncMonitorCapability(true, 48, 120, 0, 0);
        client.ProfileByOutput[output.DisplayOutputHandle] = new IntelArcSyncProfileState(CtlIntelArcSyncProfile.Recommended, 60, 120, 0, 0);
        client.ProfileAfterSet = new IntelArcSyncProfileState(CtlIntelArcSyncProfile.Excellent, 48, 120, 0, 0);

        var tweak = CreateTweak(client, [AffectedPanel]);
        tweak.Run(isEnabled: true);

        // Every native call the fake made should show up in the detailed run log with its symbolic
        // IGCL result code, not just a generic pass/fail summary.
        Assert.Contains(tweak.LastRunLog, line => line.StartsWith("ctlInit: CTL_RESULT_SUCCESS"));
        Assert.Contains(tweak.LastRunLog, line => line.StartsWith("ctlGetIntelArcSyncInfoForMonitor: CTL_RESULT_SUCCESS"));
        Assert.Contains(tweak.LastRunLog, line => line.StartsWith("ctlGetIntelArcSyncProfile: CTL_RESULT_SUCCESS"));
        Assert.Contains(tweak.LastRunLog, line => line.StartsWith("ctlSetIntelArcSyncProfile: CTL_RESULT_SUCCESS"));
    }

    [Fact]
    public void Run_NativeCallFailure_LogsRawAndSymbolicResultCode()
    {
        var client = new FakeIntelArcSyncClient
        {
            SetShouldSucceed = false,
            SetErrorMessage = "driver rejected",
            SetFailureRawCode = (int)CtlResult.ErrorUnsupportedFeature
        };
        var output = MakeOutput(10);
        client.Outputs.Add(output);
        client.CapabilityByOutput[output.DisplayOutputHandle] = new IntelArcSyncMonitorCapability(true, 48, 120, 0, 0);
        client.ProfileByOutput[output.DisplayOutputHandle] = new IntelArcSyncProfileState(CtlIntelArcSyncProfile.Recommended, 60, 120, 0, 0);

        var tweak = CreateTweak(client, [AffectedPanel]);
        tweak.Run(isEnabled: true);

        Assert.Contains(tweak.LastRunLog,
            line => line.Contains("ctlSetIntelArcSyncProfile") && line.Contains("CTL_RESULT_ERROR_UNSUPPORTED_FEATURE"));
    }

    [Fact]
    public void Run_SetCallFails_ReportsApplyFailedWithoutFalseSuccess()
    {
        var client = new FakeIntelArcSyncClient { SetShouldSucceed = false, SetErrorMessage = "driver rejected" };
        var output = MakeOutput(10);
        client.Outputs.Add(output);
        client.CapabilityByOutput[output.DisplayOutputHandle] = new IntelArcSyncMonitorCapability(true, 48, 120, 0, 0);
        client.ProfileByOutput[output.DisplayOutputHandle] = new IntelArcSyncProfileState(CtlIntelArcSyncProfile.Recommended, 60, 120, 0, 0);

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
        client.CapabilityByOutput[output.DisplayOutputHandle] = new IntelArcSyncMonitorCapability(true, 48, 120, 0, 0);
        client.ProfileByOutput[output.DisplayOutputHandle] = new IntelArcSyncProfileState(CtlIntelArcSyncProfile.Recommended, 60, 120, 0, 0);
        // Readback after SET still shows the constrained range - verification should fail.
        client.ProfileAfterSet = new IntelArcSyncProfileState(CtlIntelArcSyncProfile.Excellent, 60, 120, 0, 0);

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
        client.CapabilityByOutput[output.DisplayOutputHandle] = new IntelArcSyncMonitorCapability(true, 48, 120, 0, 0);
        client.ProfileByOutput[output.DisplayOutputHandle] = new IntelArcSyncProfileState(profile, 60, 120, 0, 0);

        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.SkippedUserProfile, result.Status);
        Assert.Equal(0, client.SetCallCount);
    }

    [Fact]
    public void Run_InvalidProfile_FailsOpenWithoutMutation()
    {
        var client = new FakeIntelArcSyncClient();
        var output = MakeOutput(10);
        client.Outputs.Add(output);
        client.CapabilityByOutput[output.DisplayOutputHandle] = new IntelArcSyncMonitorCapability(true, 48, 120, 0, 0);
        client.ProfileByOutput[output.DisplayOutputHandle] = new IntelArcSyncProfileState(CtlIntelArcSyncProfile.Invalid, 60, 120, 0, 0);

        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.UnsupportedPanel, result.Status);
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
        client.CapabilityByOutput[outputA.DisplayOutputHandle] = new IntelArcSyncMonitorCapability(true, 60, 165, 0, 0);
        client.CapabilityByOutput[outputB.DisplayOutputHandle] = new IntelArcSyncMonitorCapability(true, 40, 144, 0, 0);

        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.AmbiguousDisplay, result.Status);
        Assert.Equal(0, client.SetCallCount);
    }

    [Fact]
    public void Run_TwoCandidatesBothMatchNativeRange_ReportsAmbiguous_NoSetCall()
    {
        // Two outputs both plausible (both Arc-Sync capable AND in the native 48-120Hz class) -
        // e.g. the internal Claw panel plus an external monitor with matching capability class.
        // Per policy, this must never silently pick the first match.
        var client = new FakeIntelArcSyncClient();
        var outputA = MakeOutput(10);
        var outputB = MakeOutput(20);
        client.Outputs.Add(outputA);
        client.Outputs.Add(outputB);
        client.CapabilityByOutput[outputA.DisplayOutputHandle] = new IntelArcSyncMonitorCapability(true, 48, 120, 0, 0);
        client.CapabilityByOutput[outputB.DisplayOutputHandle] = new IntelArcSyncMonitorCapability(true, 48, 120, 0, 0);

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
        client.CapabilityByOutput[outputA.DisplayOutputHandle] = new IntelArcSyncMonitorCapability(true, 48, 120, 0, 0);
        client.CapabilityByOutput[outputB.DisplayOutputHandle] = new IntelArcSyncMonitorCapability(true, 40, 144, 0, 0);
        client.ProfileByOutput[outputA.DisplayOutputHandle] = new IntelArcSyncProfileState(CtlIntelArcSyncProfile.Recommended, 60, 120, 0, 0);
        client.ProfileAfterSet = new IntelArcSyncProfileState(CtlIntelArcSyncProfile.Excellent, 48, 120, 0, 0);

        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.Applied, result.Status);
        Assert.Equal(1, client.SetCallCount);
    }

    [Fact]
    public void Run_SingleCandidate_FriendlyNameContradictsAffectedPanel_ReportsAmbiguous_NoSetCall()
    {
        // IGCL's optional friendly name is the only correlating identity currently available; if it
        // positively contradicts the WMI-detected affected panel, don't proceed even though this is
        // the only Arc-Sync-capable native-range output.
        var client = new FakeIntelArcSyncClient();
        var output = MakeOutput(10, friendlyName: "Some Totally Different External Monitor");
        client.Outputs.Add(output);
        client.CapabilityByOutput[output.DisplayOutputHandle] = new IntelArcSyncMonitorCapability(true, 48, 120, 0, 0);

        var tweak = CreateTweak(client, [AffectedPanel]);

        var result = tweak.Run(isEnabled: true);

        Assert.Equal(IntelVrrRunStatus.AmbiguousDisplay, result.Status);
        Assert.Equal(0, client.SetCallCount);
    }
}
