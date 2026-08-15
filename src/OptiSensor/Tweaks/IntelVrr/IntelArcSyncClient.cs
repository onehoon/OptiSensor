using System.Runtime.InteropServices;

namespace OptiSensor.Tweaks.IntelVrr;

/// <summary>Arc Sync (VRR) profile values, mirroring the official IGCL <c>ctl_intel_arc_sync_profile_t</c>
/// enum from Intel's public <c>ctl_api.h</c> / arc-sync extension headers. Field layout/values must match
/// the official header - this is a from-scratch transcription, not an ad hoc simplification.</summary>
internal enum CtlIntelArcSyncProfile : uint
{
    Invalid = 0,
    Recommended = 1,
    Excellent = 2,
    Good = 3,
    Compatible = 4,
    Off = 5,
    Vesa = 6,
    Custom = 7,
    Max = 8
}

/// <summary>Generic IGCL result codes (subset of <c>ctl_result_t</c>) relevant to this feature.</summary>
internal enum CtlResult
{
    Success = 0,
    ErrorAlreadyInitialized = unchecked((int)0x70010002),
    ErrorDeviceLost = unchecked((int)0x70010003),
    ErrorOutOfHostMemory = unchecked((int)0x70010004),
    ErrorOutOfDeviceMemory = unchecked((int)0x70010005),
    ErrorInsufficientPermissions = unchecked((int)0x70010006),
    ErrorNotInitialized = unchecked((int)0x70010001),
    ErrorUninitialized = unchecked((int)0x70010008),
    ErrorUnsupportedVersion = unchecked((int)0x70010009),
    ErrorUnsupportedFeature = unchecked((int)0x70010007),
    ErrorInvalidArgument = unchecked((int)0x7001000B),
    ErrorInvalidApiHandle = unchecked((int)0x7001000C),
    ErrorInvalidNullHandle = unchecked((int)0x7001000D),
    ErrorInvalidNullPointer = unchecked((int)0x7001000E),
    ErrorInvalidSize = unchecked((int)0x7001000F),
    ErrorNotAvailable = unchecked((int)0x7001000A),
}

/// <summary>Best-effort symbolic-name resolver for <see cref="CtlResult"/>-shaped raw result codes,
/// so diagnostic logs can show e.g. "CTL_RESULT_ERROR_NOT_INITIALIZED" instead of a bare hex value.
/// Codes not present in the map are logged as "UNKNOWN" rather than dropped - this mapping is not
/// guaranteed complete against every value the real driver may return.</summary>
internal static class CtlResultNames
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [(int)CtlResult.Success] = "CTL_RESULT_SUCCESS",
        [(int)CtlResult.ErrorAlreadyInitialized] = "CTL_RESULT_ERROR_ALREADY_INITIALIZED",
        [(int)CtlResult.ErrorDeviceLost] = "CTL_RESULT_ERROR_DEVICE_LOST",
        [(int)CtlResult.ErrorOutOfHostMemory] = "CTL_RESULT_ERROR_OUT_OF_HOST_MEMORY",
        [(int)CtlResult.ErrorOutOfDeviceMemory] = "CTL_RESULT_ERROR_OUT_OF_DEVICE_MEMORY",
        [(int)CtlResult.ErrorInsufficientPermissions] = "CTL_RESULT_ERROR_INSUFFICIENT_PERMISSIONS",
        [(int)CtlResult.ErrorNotInitialized] = "CTL_RESULT_ERROR_NOT_INITIALIZED",
        [(int)CtlResult.ErrorUninitialized] = "CTL_RESULT_ERROR_UNINITIALIZED",
        [(int)CtlResult.ErrorUnsupportedVersion] = "CTL_RESULT_ERROR_UNSUPPORTED_VERSION",
        [(int)CtlResult.ErrorUnsupportedFeature] = "CTL_RESULT_ERROR_UNSUPPORTED_FEATURE",
        [(int)CtlResult.ErrorInvalidArgument] = "CTL_RESULT_ERROR_INVALID_ARGUMENT",
        [(int)CtlResult.ErrorInvalidApiHandle] = "CTL_RESULT_ERROR_INVALID_API_HANDLE",
        [(int)CtlResult.ErrorInvalidNullHandle] = "CTL_RESULT_ERROR_INVALID_NULL_HANDLE",
        [(int)CtlResult.ErrorInvalidNullPointer] = "CTL_RESULT_ERROR_INVALID_NULL_POINTER",
        [(int)CtlResult.ErrorInvalidSize] = "CTL_RESULT_ERROR_INVALID_SIZE",
        [(int)CtlResult.ErrorNotAvailable] = "CTL_RESULT_ERROR_NOT_AVAILABLE",
    };

    public static string Resolve(int rawCode) => Names.TryGetValue(rawCode, out var name) ? name : "UNKNOWN";
}

/// <summary>Diagnostic record of one native IGCL call attempt, capturing the operation name and the
/// raw/symbolic result code IGCL returned, regardless of success or failure. Used so the detailed
/// run log can show every native call made during a run, not just a generic failure message.</summary>
internal sealed record IntelArcSyncCallResult(string Operation, int RawCode, string SymbolicName, string? Detail = null)
{
    public static IntelArcSyncCallResult From(string operation, int rawCode, string? detail = null) =>
        new(operation, rawCode, CtlResultNames.Resolve(rawCode), detail);

    public override string ToString()
    {
        var detailSuffix = string.IsNullOrEmpty(Detail) ? string.Empty : $", {Detail}";
        return $"{Operation}: {SymbolicName} (0x{unchecked((uint)RawCode):X8}){detailSuffix}";
    }
}

/// <summary>Candidate IGCL display output, correlated (where possible) with a Win32 monitor identity
/// by <see cref="AffectedPanelDetector"/>.</summary>
internal sealed record IntelDisplayOutputHandle(nint DeviceHandle, nint DisplayOutputHandle, string? FriendlyName);

/// <summary>Arc Sync monitor capability, as reported by <c>ctlGetIntelArcSyncInfoForMonitor</c>
/// (<c>ctl_intel_arc_sync_monitor_params_t</c>). This is capability-only - it never reflects the
/// currently active profile/range; that comes from <see cref="IntelArcSyncProfileState"/>.</summary>
internal sealed record IntelArcSyncMonitorCapability(
    bool IsIntelArcSyncSupported,
    float MinimumRefreshRateInHz,
    float MaximumRefreshRateInHz,
    uint MaxFrameTimeIncreaseInUs,
    uint MaxFrameTimeDecreaseInUs);

/// <summary>Current Arc Sync profile/active range, as reported by <c>ctlGetIntelArcSyncProfile</c>
/// (<c>ctl_intel_arc_sync_profile_params_t</c>).</summary>
internal sealed record IntelArcSyncProfileState(
    CtlIntelArcSyncProfile Profile,
    float MinRefreshRateInHz,
    float MaxRefreshRateInHz,
    uint MaxFrameTimeIncreaseInUs,
    uint MaxFrameTimeDecreaseInUs);

/// <summary>
/// Thin abstraction over the Intel Graphics Control Library (IGCL) Arc Sync API, so
/// <see cref="IntelVrrRangeTweak"/>'s policy logic can be unit tested with a fake implementation
/// and has zero real IGCL/hardware dependency in tests.
/// </summary>
internal interface IIntelArcSyncClient : IDisposable
{
    /// <summary>Diagnostic record of every native call attempted so far on this client instance, in
    /// order, including the raw/symbolic IGCL result code for each - populated regardless of whether
    /// the call succeeded or failed, so a run's detailed log can show the full native call sequence.</summary>
    IReadOnlyList<IntelArcSyncCallResult> CallLog { get; }

    /// <summary>Initializes IGCL and enumerates devices/outputs. Returns false (without throwing)
    /// if the driver-provided library or API is unavailable.</summary>
    bool TryInitialize();

    /// <summary>All display outputs IGCL currently reports across all enumerated adapters.</summary>
    IReadOnlyList<IntelDisplayOutputHandle> EnumerateDisplayOutputs();

    /// <summary>Reads Arc Sync monitor capability for one output via <c>ctlGetIntelArcSyncInfoForMonitor</c>.
    /// Returns null if the call fails.</summary>
    IntelArcSyncMonitorCapability? TryGetMonitorCapability(IntelDisplayOutputHandle output);

    /// <summary>Reads the current Arc Sync profile/active range for one output via
    /// <c>ctlGetIntelArcSyncProfile</c>. Returns null if the call fails.</summary>
    IntelArcSyncProfileState? TryGetArcSyncProfile(IntelDisplayOutputHandle output);

    /// <summary>Applies an Arc Sync profile to one output via <c>ctlSetIntelArcSyncProfile</c>.
    /// Returns (success, errorMessage).</summary>
    (bool Success, string? Error) TrySetArcSyncProfile(IntelDisplayOutputHandle output, CtlIntelArcSyncProfile profile);
}

/// <summary>
/// Real IGCL-backed implementation. All native interop is isolated behind this class - policy
/// logic in <see cref="IntelVrrRangeTweak"/> never calls into <c>ControlLib.dll</c> directly.
///
/// IMPORTANT: struct layouts below (including the mandatory <c>Size</c>/<c>Version</c> header
/// fields IGCL structs require) are transcribed from Intel's public IGCL headers
/// (ctl_api.h / ctl_arc_sync_profile / related display extension headers) as accurately as
/// possible from documentation. They must be kept in sync with the official headers if Intel
/// revises them - do not "simplify" these layouts. Notably: monitor capability and current
/// profile/range are two DISTINCT official structs/calls (ctl_intel_arc_sync_monitor_params_t via
/// ctlGetIntelArcSyncInfoForMonitor, and ctl_intel_arc_sync_profile_params_t via
/// ctlGetIntelArcSyncProfile / ctlSetIntelArcSyncProfile) - they must not be merged into one
/// ad hoc struct. Native <c>bool</c> fields are 1 byte (marshaled as U1), unlike Win32 BOOL which
/// is a 4-byte int - do not use UnmanagedType.Bool for these.
/// </summary>
internal sealed class IntelArcSyncClient : IIntelArcSyncClient
{
    private const string ControlLibDll = "ControlLib.dll";
    private const byte CtlInitVersion = 0; // ctl_init_args_t.Version - 0 selects the latest supported ABI.

    private bool _initialized;
    private nint[] _adapterHandles = [];
    private readonly List<IntelArcSyncCallResult> _callLog = [];

    public IReadOnlyList<IntelArcSyncCallResult> CallLog => _callLog;

    private void RecordCall(string operation, int rawCode, string? detail = null) =>
        _callLog.Add(IntelArcSyncCallResult.From(operation, rawCode, detail));

    [StructLayout(LayoutKind.Sequential)]
    private struct CtlInitArgs
    {
        public uint Size;
        public byte Version;
        /// <summary>Single 32-bit encoded API version (major/minor packed into one value), per
        /// Intel's public ctl_api.h - not three separate Major/Minor/Build fields.</summary>
        public uint AppVersion;
        public uint Flags;
        public uint SupportedVersion;
        /// <summary>ctl_application_id_t UnlockCapsID, per Intel's public header - not an
        /// application-identity field; it unlocks capability tiers.</summary>
        public CtlApplicationId UnlockCapsID;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CtlApplicationId
    {
        public Guid Id;
    }

    /// <summary>ctl_intel_arc_sync_monitor_params_t - monitor capability only.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CtlIntelArcSyncMonitorParams
    {
        public uint Size;
        public byte Version;
        [MarshalAs(UnmanagedType.U1)] public bool IsIntelArcSyncSupported;
        public float MinimumRefreshRateInHz;
        public float MaximumRefreshRateInHz;
        public uint MaxFrameTimeIncreaseInUs;
        public uint MaxFrameTimeDecreaseInUs;
    }

    /// <summary>ctl_intel_arc_sync_profile_params_t - current profile/active range. Used for both
    /// ctlGetIntelArcSyncProfile (read) and ctlSetIntelArcSyncProfile (set), matching the official
    /// entry points, which each take this same params struct.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CtlIntelArcSyncProfileParams
    {
        public uint Size;
        public byte Version;
        public CtlIntelArcSyncProfile Profile;
        public float MaxRefreshRateInHz;
        public float MinRefreshRateInHz;
        public uint MaxFrameTimeIncreaseInUs;
        public uint MaxFrameTimeDecreaseInUs;
    }

    // NOTE: These entry points/signatures follow the naming and general shape of IGCL's
    // display/arc-sync extension. Exact export ordinal/signature must be validated against
    // the driver-installed ControlLib.dll on real hardware before this is trusted in production.
    [DllImport(ControlLibDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ctlInit(ref CtlInitArgs args, out nint apiHandle);

    [DllImport(ControlLibDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ctlClose(nint apiHandle);

    [DllImport(ControlLibDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ctlEnumerateDevices(nint apiHandle, ref uint count, [Out] nint[]? adapters);

    [DllImport(ControlLibDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ctlEnumerateDisplayOutputs(nint adapterHandle, ref uint count, [Out] nint[]? displayOutputs);

    [DllImport(ControlLibDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ctlGetIntelArcSyncInfoForMonitor(nint displayOutputHandle, ref CtlIntelArcSyncMonitorParams monitorParams);

    [DllImport(ControlLibDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ctlGetIntelArcSyncProfile(nint displayOutputHandle, ref CtlIntelArcSyncProfileParams profileParams);

    [DllImport(ControlLibDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ctlSetIntelArcSyncProfile(nint displayOutputHandle, ref CtlIntelArcSyncProfileParams profileParams);

    private nint _apiHandle;

    public bool TryInitialize()
    {
        try
        {
            var initArgs = new CtlInitArgs
            {
                Size = (uint)Marshal.SizeOf<CtlInitArgs>(),
                Version = CtlInitVersion,
                AppVersion = 0x00010000, // encoded 1.0
                SupportedVersion = 0x00010000,
                UnlockCapsID = new CtlApplicationId { Id = Guid.Empty }
            };

            var result = ctlInit(ref initArgs, out _apiHandle);
            RecordCall("ctlInit", result);
            if (result != (int)CtlResult.Success || _apiHandle == 0)
                return false;

            // From this point on, _apiHandle is a live native handle. Even if the remaining steps
            // below fail and this method returns false, Dispose() must still close it - cleanup is
            // gated on _apiHandle, not on _initialized (which only turns true on full success).

            uint adapterCount = 0;
            var enumCountResult = ctlEnumerateDevices(_apiHandle, ref adapterCount, null);
            RecordCall("ctlEnumerateDevices", enumCountResult, $"count={adapterCount}");
            if (enumCountResult != (int)CtlResult.Success || adapterCount == 0)
                return false;

            var adapters = new nint[adapterCount];
            var enumResult = ctlEnumerateDevices(_apiHandle, ref adapterCount, adapters);
            RecordCall("ctlEnumerateDevices", enumResult, $"count={adapterCount}");
            if (enumResult != (int)CtlResult.Success)
                return false;

            _adapterHandles = adapters;
            _initialized = true;
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public IReadOnlyList<IntelDisplayOutputHandle> EnumerateDisplayOutputs()
    {
        if (!_initialized)
            return [];

        var results = new List<IntelDisplayOutputHandle>();
        foreach (var adapter in _adapterHandles)
        {
            uint count = 0;
            var countResult = ctlEnumerateDisplayOutputs(adapter, ref count, null);
            RecordCall("ctlEnumerateDisplayOutputs", countResult, $"adapter={adapter}, count={count}");
            if (countResult != (int)CtlResult.Success || count == 0)
                continue;

            var outputs = new nint[count];
            var enumResult = ctlEnumerateDisplayOutputs(adapter, ref count, outputs);
            RecordCall("ctlEnumerateDisplayOutputs", enumResult, $"adapter={adapter}, count={count}");
            if (enumResult != (int)CtlResult.Success)
                continue;

            foreach (var output in outputs)
                results.Add(new IntelDisplayOutputHandle(adapter, output, null));
        }

        return results;
    }

    public IntelArcSyncMonitorCapability? TryGetMonitorCapability(IntelDisplayOutputHandle output)
    {
        try
        {
            var monitorParams = new CtlIntelArcSyncMonitorParams
            {
                Size = (uint)Marshal.SizeOf<CtlIntelArcSyncMonitorParams>(),
                Version = 0
            };

            var result = ctlGetIntelArcSyncInfoForMonitor(output.DisplayOutputHandle, ref monitorParams);
            RecordCall("ctlGetIntelArcSyncInfoForMonitor", result, $"output={output.DisplayOutputHandle}");
            if (result != (int)CtlResult.Success)
                return null;

            return new IntelArcSyncMonitorCapability(
                monitorParams.IsIntelArcSyncSupported,
                monitorParams.MinimumRefreshRateInHz,
                monitorParams.MaximumRefreshRateInHz,
                monitorParams.MaxFrameTimeIncreaseInUs,
                monitorParams.MaxFrameTimeDecreaseInUs);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public IntelArcSyncProfileState? TryGetArcSyncProfile(IntelDisplayOutputHandle output)
    {
        try
        {
            var profileParams = new CtlIntelArcSyncProfileParams
            {
                Size = (uint)Marshal.SizeOf<CtlIntelArcSyncProfileParams>(),
                Version = 0
            };

            var result = ctlGetIntelArcSyncProfile(output.DisplayOutputHandle, ref profileParams);
            RecordCall("ctlGetIntelArcSyncProfile", result, $"output={output.DisplayOutputHandle}");
            if (result != (int)CtlResult.Success)
                return null;

            return new IntelArcSyncProfileState(
                profileParams.Profile,
                profileParams.MinRefreshRateInHz,
                profileParams.MaxRefreshRateInHz,
                profileParams.MaxFrameTimeIncreaseInUs,
                profileParams.MaxFrameTimeDecreaseInUs);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public (bool Success, string? Error) TrySetArcSyncProfile(IntelDisplayOutputHandle output, CtlIntelArcSyncProfile profile)
    {
        try
        {
            var profileParams = new CtlIntelArcSyncProfileParams
            {
                Size = (uint)Marshal.SizeOf<CtlIntelArcSyncProfileParams>(),
                Version = 0,
                Profile = profile
            };

            var result = ctlSetIntelArcSyncProfile(output.DisplayOutputHandle, ref profileParams);
            RecordCall("ctlSetIntelArcSyncProfile", result, $"output={output.DisplayOutputHandle}");
            return result == (int)CtlResult.Success
                ? (true, null)
                : (false, $"{CtlResultNames.Resolve(result)} (0x{result:X8})");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public void Dispose()
    {
        // Gated on "did we successfully acquire a handle from ctlInit", NOT on _initialized (which
        // only becomes true after every subsequent init step also succeeds). Otherwise a partial
        // init - ctlInit succeeds but a later enumeration step fails - leaks the native handle,
        // because the failed TryInitialize() call returns false without ever calling ctlClose, and
        // the caller's `using` block then disposes an object whose Dispose() used to no-op.
        if (_apiHandle != 0)
        {
            try
            {
                var result = ctlClose(_apiHandle);
                RecordCall("ctlClose", result);
            }
            catch (Exception)
            {
                // Best-effort cleanup only.
            }
        }

        _initialized = false;
        _apiHandle = 0;
        _adapterHandles = [];
    }
}
