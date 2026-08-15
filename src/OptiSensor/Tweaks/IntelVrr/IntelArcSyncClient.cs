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
    ErrorNotInitialized = unchecked((int)0x70010001),
    ErrorUnsupportedFeature = unchecked((int)0x70010007),
    ErrorNotAvailable = unchecked((int)0x7001000A),
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
        public CtlApplicationId ApplicationId;
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
                ApplicationId = new CtlApplicationId { Id = Guid.Empty }
            };

            var result = ctlInit(ref initArgs, out _apiHandle);
            if (result != (int)CtlResult.Success || _apiHandle == 0)
                return false;

            uint adapterCount = 0;
            if (ctlEnumerateDevices(_apiHandle, ref adapterCount, null) != (int)CtlResult.Success || adapterCount == 0)
                return false;

            var adapters = new nint[adapterCount];
            if (ctlEnumerateDevices(_apiHandle, ref adapterCount, adapters) != (int)CtlResult.Success)
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
            if (ctlEnumerateDisplayOutputs(adapter, ref count, null) != (int)CtlResult.Success || count == 0)
                continue;

            var outputs = new nint[count];
            if (ctlEnumerateDisplayOutputs(adapter, ref count, outputs) != (int)CtlResult.Success)
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
            return result == (int)CtlResult.Success
                ? (true, null)
                : (false, $"ctlSetIntelArcSyncProfile returned 0x{result:X8}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public void Dispose()
    {
        if (_initialized && _apiHandle != 0)
        {
            try
            {
                ctlClose(_apiHandle);
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
