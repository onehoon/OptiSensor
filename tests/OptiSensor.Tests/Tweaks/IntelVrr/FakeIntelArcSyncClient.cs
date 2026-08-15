using OptiSensor.Tweaks.IntelVrr;

namespace OptiSensor.Tests.Tweaks.IntelVrr;

/// <summary>Fake IGCL client used to drive <see cref="IntelVrrRangeTweak"/> policy tests without
/// any real IGCL/hardware dependency.</summary>
internal sealed class FakeIntelArcSyncClient : IIntelArcSyncClient
{
    public bool InitializeResult { get; set; } = true;
    public List<IntelDisplayOutputHandle> Outputs { get; } = [];
    public Dictionary<nint, IntelArcSyncMonitorCapability> CapabilityByOutput { get; } = [];
    public Dictionary<nint, IntelArcSyncProfileState> ProfileByOutput { get; } = [];
    public bool SetShouldSucceed { get; set; } = true;
    public string? SetErrorMessage { get; set; }
    public IntelArcSyncProfileState? ProfileAfterSet { get; set; }
    public CtlIntelArcSyncProfile? LastSetProfile { get; private set; }
    public int SetCallCount { get; private set; }
    public bool Disposed { get; private set; }

    public bool TryInitialize() => InitializeResult;

    public IReadOnlyList<IntelDisplayOutputHandle> EnumerateDisplayOutputs() => Outputs;

    public IntelArcSyncMonitorCapability? TryGetMonitorCapability(IntelDisplayOutputHandle output)
    {
        return CapabilityByOutput.TryGetValue(output.DisplayOutputHandle, out var capability) ? capability : null;
    }

    public IntelArcSyncProfileState? TryGetArcSyncProfile(IntelDisplayOutputHandle output)
    {
        // After a successful SET, subsequent reads should reflect the post-set state.
        if (ProfileAfterSet is not null && SetCallCount > 0)
            return ProfileAfterSet;

        return ProfileByOutput.TryGetValue(output.DisplayOutputHandle, out var profile) ? profile : null;
    }

    public (bool Success, string? Error) TrySetArcSyncProfile(IntelDisplayOutputHandle output, CtlIntelArcSyncProfile profile)
    {
        SetCallCount++;
        LastSetProfile = profile;
        return SetShouldSucceed ? (true, null) : (false, SetErrorMessage ?? "set failed");
    }

    public void Dispose() => Disposed = true;
}
