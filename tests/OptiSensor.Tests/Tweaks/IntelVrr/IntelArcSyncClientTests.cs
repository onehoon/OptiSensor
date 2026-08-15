using OptiSensor.Tweaks.IntelVrr;
using Xunit;

namespace OptiSensor.Tests.Tweaks.IntelVrr;

/// <summary>
/// Unit tests for the pieces of <see cref="IntelArcSyncClient"/> that don't require the real
/// ControlLib.dll P/Invoke boundary: the symbolic result-code mapping and call-result formatting
/// used to build the detailed diagnostic log (review round 2, Major 3).
///
/// NOTE on the handle-leak fix (review round 2, Major 2): <c>IntelArcSyncClient.Dispose()</c> now
/// gates <c>ctlClose</c> on "did we acquire a native handle" (<c>_apiHandle != 0</c>) rather than on
/// the full-success <c>_initialized</c> flag, so a partial init (ctlInit succeeds, a later
/// enumeration step fails) still closes the handle instead of leaking it. That change lives entirely
/// on the far side of a `DllImport("ControlLib.dll")` boundary that is not present/loadable in this
/// test environment (or CI), so it cannot be exercised behaviorally here without either shipping a
/// fake native DLL or restructuring the class to inject the native calls - both out of scope for
/// this fix. The guarantee is structural instead: <c>Dispose()</c> contains a single
/// unconditional <c>if (_apiHandle != 0)</c> check around the <c>ctlClose</c> call, independent of
/// <c>_initialized</c>, and every early-return failure path inside <c>TryInitialize()</c> after
/// <c>ctlInit</c> succeeds returns from the same method whose only cleanup path is the caller's
/// <c>using</c>-scoped <c>Dispose()</c> - see the comments in IntelArcSyncClient.cs.
/// </summary>
public class IntelArcSyncClientTests
{
    [Fact]
    public void CtlResultNames_Resolve_KnownSuccessCode_ReturnsSymbolicName()
    {
        Assert.Equal("CTL_RESULT_SUCCESS", CtlResultNames.Resolve((int)CtlResult.Success));
    }

    [Fact]
    public void CtlResultNames_Resolve_KnownErrorCode_ReturnsSymbolicName()
    {
        Assert.Equal("CTL_RESULT_ERROR_UNSUPPORTED_FEATURE", CtlResultNames.Resolve((int)CtlResult.ErrorUnsupportedFeature));
        Assert.Equal("CTL_RESULT_ERROR_NOT_INITIALIZED", CtlResultNames.Resolve((int)CtlResult.ErrorNotInitialized));
        Assert.Equal("CTL_RESULT_ERROR_NOT_AVAILABLE", CtlResultNames.Resolve((int)CtlResult.ErrorNotAvailable));
    }

    [Fact]
    public void CtlResultNames_Resolve_UnknownCode_ReturnsUnknownMarker_NotDropped()
    {
        Assert.Equal("UNKNOWN", CtlResultNames.Resolve(unchecked((int)0x7BADF00D)));
    }

    [Fact]
    public void IntelArcSyncCallResult_From_ResolvesSymbolicNameAutomatically()
    {
        var result = IntelArcSyncCallResult.From("ctlInit", (int)CtlResult.ErrorNotInitialized);

        Assert.Equal("ctlInit", result.Operation);
        Assert.Equal("CTL_RESULT_ERROR_NOT_INITIALIZED", result.SymbolicName);
    }

    [Fact]
    public void IntelArcSyncCallResult_ToString_IncludesOperationSymbolicNameHexCodeAndDetail()
    {
        var result = IntelArcSyncCallResult.From("ctlEnumerateDevices", (int)CtlResult.Success, "count=1");

        Assert.Equal("ctlEnumerateDevices: CTL_RESULT_SUCCESS (0x00000000), count=1", result.ToString());
    }

    [Fact]
    public void IntelArcSyncCallResult_ToString_UnknownCode_ShowsUnknownAndHexValue()
    {
        var result = IntelArcSyncCallResult.From("ctlGetIntelArcSyncInfoForMonitor", unchecked((int)0x70000123));

        Assert.Contains("UNKNOWN", result.ToString());
        Assert.Contains("0x70000123", result.ToString());
    }
}
