using System.Runtime.InteropServices;

namespace OptiSensor.Claw;

/// <summary>
/// Intel IGCL GPU telemetry. Direct C# port of ClawHUD's <c>IgclGpuTelemetrySampler</c>
/// production path, which exposes exactly GPU usage % and GPU clock MHz - nothing else from
/// <c>ctlPowerTelemetryGetV2</c>. <c>null</c> fields mean the value was unavailable for this
/// sample; a genuine numeric 0 remains a value.
/// </summary>
internal sealed record IgclGpuTelemetrySnapshot(
    double? GpuUsagePercent,
    double? GpuClockMHz);

internal sealed class IgclGpuTelemetryReader : IDisposable
{
    private const uint CtlSuccess = 0;
    private const uint ApiVersion = (1u << 16) | 1u; // IGCL API 1.1
    private const uint UseLevelZero = 0x1;           // CTL_INIT_FLAG_USE_LEVEL_ZERO
    private const byte TelemetryVersion = 1;
    private const uint LoadLibrarySearchSystem32 = 0x00000800;

    private nint _library;
    private nint _api;
    private nint _device;
    private CloseFn? _close;
    private TelemetryFn? _telemetry;
    private readonly IgclGpuUsageTracker _usage = new();
    private bool _initializationAttempted;

    public bool InitializationAttempted => _initializationAttempted;
    public bool Initialized => _library != nint.Zero;

    /// <summary>
    /// Loads <c>ControlLib.dll</c> from System32, resolves the four production entry points,
    /// initializes IGCL (API 1.1, Level Zero), and selects the first enumerated device.
    /// </summary>
    public bool Initialize()
    {
        Reset();
        _initializationAttempted = true;

        _library = LoadLibraryExW("ControlLib.dll", nint.Zero, LoadLibrarySearchSystem32);
        if (_library == nint.Zero)
            return Fail();

        var init = GetExport<InitFn>("ctlInit");
        _close = GetExport<CloseFn>("ctlClose");
        var enumerate = GetExport<EnumDevicesFn>("ctlEnumerateDevices");
        _telemetry = GetExport<TelemetryFn>("ctlPowerTelemetryGetV2");
        if (init is null || _close is null || enumerate is null || _telemetry is null)
            return Fail();

        var args = new InitArgs
        {
            Size = (uint)Marshal.SizeOf<InitArgs>(),
            AppVersion = ApiVersion,
            Flags = UseLevelZero,
            ApplicationUid = new byte[16],
        };
        if (init(ref args, out _api) != CtlSuccess)
            return Fail();

        uint count = 0;
        if (enumerate(_api, ref count, null) != CtlSuccess || count == 0)
            return Fail();

        var devices = new nint[count];
        if (enumerate(_api, ref count, devices) != CtlSuccess || count == 0)
            return Fail();

        _device = devices[0];
        return true;
    }

    /// <summary>
    /// Collects one telemetry sample. The first valid sample establishes the usage delta
    /// baseline and returns <c>GpuUsagePercent = null</c>. Clock and usage are decoded
    /// independently - one being unavailable does not invalidate the other.
    /// </summary>
    public IgclGpuTelemetrySnapshot? Sample()
    {
        if (!Initialized || _telemetry is null || _device == nint.Zero)
            return null;

        var telemetry = new PowerTelemetryV2
        {
            Size = (uint)Marshal.SizeOf<PowerTelemetryV2>(),
            Version = TelemetryVersion,
        };

        if (_telemetry(_device, ref telemetry) != CtlSuccess)
        {
            _usage.Reset();
            return null;
        }

        var clock = DecodeItem(telemetry.GpuCurrentClockFrequency);
        var gpuClockMHz = clock is { } c && c >= 0.0 ? clock : null;

        var gpuUsagePercent = _usage.Update(
            DecodeItem(telemetry.TimeStamp),
            DecodeItem(telemetry.RenderComputeActivityCounter));

        return new IgclGpuTelemetrySnapshot(gpuUsagePercent, gpuClockMHz);
    }

    public void Dispose() => Reset();

    private bool Fail()
    {
        Reset();
        _initializationAttempted = true;
        return false;
    }

    private void Reset()
    {
        _usage.Reset();

        if (_close is not null && _api != nint.Zero)
            _close(_api);

        _close = null;
        _telemetry = null;
        _api = nint.Zero;
        _device = nint.Zero;

        if (_library != nint.Zero)
            FreeLibrary(_library);
        _library = nint.Zero;

        _initializationAttempted = false;
    }

    private T? GetExport<T>(string name) where T : Delegate
    {
        var address = GetProcAddress(_library, name);
        return address == nint.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    /// <summary>
    /// Decodes an IGCL telemetry item to a <see cref="double"/>. Unsupported items, unknown
    /// numeric types, and non-finite values are unavailable; a real numeric 0 is valid.
    /// </summary>
    internal static double? DecodeItem(in IgclItem item)
    {
        if (item.Supported == 0)
            return null;

        double? value = item.Type switch
        {
            0 => item.I8,
            1 => item.U8,
            2 => item.I16,
            3 => item.U16,
            4 => item.I32,
            5 => item.U32,
            6 => item.I64,
            7 => item.U64,
            8 => item.F,
            9 => item.D,
            _ => null,
        };

        return value is { } v && double.IsFinite(v) ? v : null;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint InitFn(ref InitArgs args, out nint apiHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint CloseFn(nint apiHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint EnumDevicesFn(nint apiHandle, ref uint count, [Out] nint[]? devices);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint TelemetryFn(nint device, ref PowerTelemetryV2 telemetry);

    [StructLayout(LayoutKind.Sequential)]
    private struct InitArgs
    {
        public uint Size;
        public byte Version;
        public uint AppVersion;
        public uint Flags;
        public uint SupportedVersion;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ApplicationUid;
    }

    // Minimal mirror of the pinned Intel IGCL v1.1 read-only ABI (igcl_api.h): only the
    // fields on the ClawHUD production path. sizeof(Item) == 24, value union at offset 16.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct IgclItem
    {
        [FieldOffset(0)] public byte Supported;
        [FieldOffset(4)] public uint Units;
        [FieldOffset(8)] public uint Type;
        [FieldOffset(16)] public sbyte I8;
        [FieldOffset(16)] public byte U8;
        [FieldOffset(16)] public short I16;
        [FieldOffset(16)] public ushort U16;
        [FieldOffset(16)] public int I32;
        [FieldOffset(16)] public uint U32;
        [FieldOffset(16)] public long I64;
        [FieldOffset(16)] public ulong U64;
        [FieldOffset(16)] public float F;
        [FieldOffset(16)] public double D;
    }

    // sizeof(PowerTelemetryV2) == 1016; each Item is 8-aligned, so the first Item (timeStamp)
    // sits at offset 8 and the rest follow in 24-byte steps. Only the three consumed fields
    // are declared; the remainder is opaque padding covered by Size.
    [StructLayout(LayoutKind.Explicit, Size = 1016)]
    private struct PowerTelemetryV2
    {
        [FieldOffset(0)] public uint Size;
        [FieldOffset(4)] public byte Version;
        [FieldOffset(8)] public IgclItem TimeStamp;
        [FieldOffset(80)] public IgclItem GpuCurrentClockFrequency;
        [FieldOffset(152)] public IgclItem RenderComputeActivityCounter;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryExW(string lpLibFileName, nint hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint hModule);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern nint GetProcAddress(nint hModule, string procName);
}

/// <summary>
/// The GPU-usage delta state machine from ClawHUD's <c>IgclGpuTelemetrySampler</c>: keeps exactly
/// one previous <c>(timestamp, activity)</c> pair and turns two cumulative samples into a
/// clamped 0..100 usage. Invalid/missing inputs and telemetry-call failures reset the baseline,
/// so usage stays unavailable until a fresh baseline is established.
/// </summary>
internal sealed class IgclGpuUsageTracker
{
    private double? _previousTimestamp;
    private double? _previousActivity;

    public void Reset()
    {
        _previousTimestamp = null;
        _previousActivity = null;
    }

    public double? Update(double? timestamp, double? activity)
    {
        if (timestamp is not { } currentTimestamp || activity is not { } currentActivity ||
            currentTimestamp < 0.0 || currentActivity < 0.0)
        {
            Reset();
            return null;
        }

        double? usage = null;
        if (_previousTimestamp is { } previousTimestamp && _previousActivity is { } previousActivity)
            usage = CalculateGpuUsage(previousTimestamp, previousActivity, currentTimestamp, currentActivity);

        _previousTimestamp = currentTimestamp;
        _previousActivity = currentActivity;
        return usage;
    }

    /// <summary>
    /// <c>(deltaActivity / deltaTimestamp) * 100</c>, clamped to 0..100. Non-finite inputs,
    /// non-increasing time, activity rollback, or a non-finite result -> unavailable.
    /// </summary>
    internal static double? CalculateGpuUsage(
        double previousTimestamp, double previousActivity,
        double currentTimestamp, double currentActivity)
    {
        if (!double.IsFinite(previousTimestamp) || !double.IsFinite(previousActivity) ||
            !double.IsFinite(currentTimestamp) || !double.IsFinite(currentActivity) ||
            currentTimestamp <= previousTimestamp || currentActivity < previousActivity)
        {
            return null;
        }

        var usage = (currentActivity - previousActivity) / (currentTimestamp - previousTimestamp) * 100.0;
        if (!double.IsFinite(usage))
            return null;

        return Math.Clamp(usage, 0.0, 100.0);
    }
}
