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
    public int? SetFailureRawCode { get; set; }

    private readonly List<IntelArcSyncCallResult> _callLog = [];
    public IReadOnlyList<IntelArcSyncCallResult> CallLog => _callLog;

    public bool TryInitialize()
    {
        _callLog.Add(IntelArcSyncCallResult.From("ctlInit",
            InitializeResult ? (int)CtlResult.Success : (int)CtlResult.ErrorNotAvailable));
        return InitializeResult;
    }

    public IReadOnlyList<IntelDisplayOutputHandle> EnumerateDisplayOutputs()
    {
        _callLog.Add(IntelArcSyncCallResult.From("ctlEnumerateDisplayOutputs", (int)CtlResult.Success, $"count={Outputs.Count}"));
        return Outputs;
    }

    public IntelArcSyncMonitorCapability? TryGetMonitorCapability(IntelDisplayOutputHandle output)
    {
        var found = CapabilityByOutput.TryGetValue(output.DisplayOutputHandle, out var capability);
        _callLog.Add(IntelArcSyncCallResult.From("ctlGetIntelArcSyncInfoForMonitor",
            found ? (int)CtlResult.Success : (int)CtlResult.ErrorNotAvailable, $"output={output.DisplayOutputHandle}"));
        return found ? capability : null;
    }

    public IntelArcSyncProfileState? TryGetArcSyncProfile(IntelDisplayOutputHandle output)
    {
        // After a successful SET, subsequent reads should reflect the post-set state.
        IntelArcSyncProfileState? profile = ProfileAfterSet is not null && SetCallCount > 0
            ? ProfileAfterSet
            : ProfileByOutput.TryGetValue(output.DisplayOutputHandle, out var stored) ? stored : null;

        _callLog.Add(IntelArcSyncCallResult.From("ctlGetIntelArcSyncProfile",
            profile is not null ? (int)CtlResult.Success : (int)CtlResult.ErrorNotAvailable, $"output={output.DisplayOutputHandle}"));
        return profile;
    }

    public (bool Success, string? Error) TrySetArcSyncProfile(IntelDisplayOutputHandle output, CtlIntelArcSyncProfile profile)
    {
        SetCallCount++;
        LastSetProfile = profile;
        var rawCode = SetShouldSucceed ? (int)CtlResult.Success : SetFailureRawCode ?? (int)CtlResult.ErrorNotAvailable;
        _callLog.Add(IntelArcSyncCallResult.From("ctlSetIntelArcSyncProfile", rawCode, $"output={output.DisplayOutputHandle}"));
        return SetShouldSucceed ? (true, null) : (false, SetErrorMessage ?? "set failed");
    }

    public void Dispose() => Disposed = true;
}
