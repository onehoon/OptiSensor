using OptiSensor.Tweaks.IntelVrr;

namespace OptiSensor.Tests.Tweaks.IntelVrr;

/// <summary>Fake IGCL client used to drive <see cref="IntelVrrRangeTweak"/> policy tests without
/// any real IGCL/hardware dependency.</summary>
internal sealed class FakeIntelArcSyncClient : IIntelArcSyncClient
{
    public bool InitializeResult { get; set; } = true;
    public List<IntelDisplayOutputHandle> Outputs { get; } = [];
    public Dictionary<nint, IntelArcSyncInfo> InfoByOutput { get; } = [];
    public bool SetShouldSucceed { get; set; } = true;
    public string? SetErrorMessage { get; set; }
    public IntelArcSyncInfo? InfoAfterSet { get; set; }
    public CtlIntelArcSyncProfile? LastSetProfile { get; private set; }
    public int SetCallCount { get; private set; }
    public bool Disposed { get; private set; }

    public bool TryInitialize() => InitializeResult;

    public IReadOnlyList<IntelDisplayOutputHandle> EnumerateDisplayOutputs() => Outputs;

    public IntelArcSyncInfo? TryGetArcSyncInfo(IntelDisplayOutputHandle output)
    {
        // After a successful SET, subsequent reads should reflect the post-set state.
        if (InfoAfterSet is not null && SetCallCount > 0)
            return InfoAfterSet;

        return InfoByOutput.TryGetValue(output.DisplayOutputHandle, out var info) ? info : null;
    }

    public (bool Success, string? Error) TrySetArcSyncProfile(IntelDisplayOutputHandle output, CtlIntelArcSyncProfile profile)
    {
        SetCallCount++;
        LastSetProfile = profile;
        return SetShouldSucceed ? (true, null) : (false, SetErrorMessage ?? "set failed");
    }

    public void Dispose() => Disposed = true;
}
