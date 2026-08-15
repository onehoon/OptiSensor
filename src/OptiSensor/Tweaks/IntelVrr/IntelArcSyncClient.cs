using System.Runtime.InteropServices;

namespace OptiSensor.Tweaks.IntelVrr;

/// <summary>Arc Sync (VRR) profile values, mirroring the official IGCL <c>ctl_intel_arc_sync_profile_t</c>
/// enum from Intel's public <c>ctl_api.h</c> / arc-sync extension headers. Field layout/values must match
/// the official header - this is a from-scratch transcription, not an ad hoc simplification.</summary>
internal enum CtlIntelArcSyncProfile
{
    Default = 0,
    Basic = 1,
    Excellent = 2,
    Custom = 3,
    Off = 4
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

/// <summary>Arc Sync capability/state for one display output, as reported by IGCL.</summary>
internal sealed record IntelArcSyncInfo(
    bool IsArcSyncSupported,
    double CapabilityMinRefreshHz,
    double CapabilityMaxRefreshHz,
    CtlIntelArcSyncProfile CurrentProfile,
    double CurrentMinRefreshHz,
    double CurrentMaxRefreshHz);

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

    /// <summary>Reads Arc Sync capability/state for one output. Returns null if the call fails or
    /// the output does not support Arc Sync.</summary>
    IntelArcSyncInfo? TryGetArcSyncInfo(IntelDisplayOutputHandle output);

    /// <summary>Applies an Arc Sync profile to one output. Returns (success, errorMessage).</summary>
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
/// revises them - do not "simplify" these layouts.
/// </summary>
internal sealed class IntelArcSyncClient : IIntelArcSyncClient
{
    private const string ControlLibDll = "ControlLib.dll";
    private const uint CtlImpiVersion = 0; // ctl_init_args_t.Version - 0 selects the latest supported ABI.

    private bool _initialized;
    private nint[] _adapterHandles = [];

    [StructLayout(LayoutKind.Sequential)]
    private struct CtlInitArgs
    {
        public uint Size;
        public uint Version;
        public CtlApiVersion AppVersion;
        public uint Flags;
        public CtlApiVersion SupportedVersion;
        public CtlApplicationId ApplicationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CtlApiVersion
    {
        public uint Major;
        public uint Minor;
        public uint Build;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CtlApplicationId
    {
        public Guid Id;
    }

    /// <summary>ctl_intel_arc_sync_info_t (approximate transcription: mandatory Size/Version header
    /// followed by capability and current-state fields).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CtlIntelArcSyncInfo
    {
        public uint Size;
        public uint Version;
        [MarshalAs(UnmanagedType.Bool)] public bool SupportedArcSync;
        public double MinRefreshRateCapable;
        public double MaxRefreshRateCapable;
        public CtlIntelArcSyncProfile CurrentProfile;
        public double CurrentMinRefreshRate;
        public double CurrentMaxRefreshRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CtlIntelArcSyncProfileParams
    {
        public uint Size;
        public uint Version;
        public CtlIntelArcSyncProfile Profile;
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
    private static extern int ctlGetIntelArcSyncInfoForMonitor(nint displayOutputHandle, ref CtlIntelArcSyncInfo info);

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
                Version = CtlImpiVersion,
                AppVersion = new CtlApiVersion { Major = 1, Minor = 0, Build = 0 },
                SupportedVersion = new CtlApiVersion { Major = 1, Minor = 0, Build = 0 },
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

    public IntelArcSyncInfo? TryGetArcSyncInfo(IntelDisplayOutputHandle output)
    {
        try
        {
            var info = new CtlIntelArcSyncInfo
            {
                Size = (uint)Marshal.SizeOf<CtlIntelArcSyncInfo>(),
                Version = 0
            };

            var result = ctlGetIntelArcSyncInfoForMonitor(output.DisplayOutputHandle, ref info);
            if (result != (int)CtlResult.Success)
                return null;

            return new IntelArcSyncInfo(
                info.SupportedArcSync,
                info.MinRefreshRateCapable,
                info.MaxRefreshRateCapable,
                info.CurrentProfile,
                info.CurrentMinRefreshRate,
                info.CurrentMaxRefreshRate);
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
