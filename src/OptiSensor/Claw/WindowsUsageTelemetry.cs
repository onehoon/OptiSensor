using System.Runtime.InteropServices;

namespace OptiSensor.Claw;

/// <summary>Windows adapter LUID (<c>LowPart</c> DWORD, <c>HighPart</c> LONG).</summary>
internal readonly record struct Luid(uint LowPart, int HighPart);

/// <summary>
/// Windows CPU-utility, physical-memory-used, and Intel GPU-memory-used telemetry. Direct C#
/// port of ClawHUD's <c>WindowsUsageSampler</c>. <c>null</c> fields mean the value was
/// unavailable for this sample; a genuine 0 remains a value.
/// </summary>
internal sealed record WindowsUsageSnapshot(
    double? CpuUsagePercent,
    ulong? SystemMemoryUsedBytes,
    ulong? IntelGpuMemoryUsedBytes);

internal sealed class WindowsUsageTelemetryReader : IDisposable
{
    // Exact ClawHUD counter: turbo/utility can exceed 100 and is capped, not rejected.
    private const string CpuCounterPath = @"\Processor Information(_Total)\% Processor Utility";
    private const string DedicatedUsageWildcard = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private const string SharedUsageWildcard = @"\GPU Adapter Memory(*)\Shared Usage";
    private const int MaxIntelMemoryRebindAttempts = 3;

    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhFmtLarge = 0x00000400;
    private const uint ErrorSuccess = 0;
    private const uint PdhCstatusNewData = 0x00000001;
    private const uint PdhMoreData = 0x800007D2;

    private const uint IntelVendorId = 0x8086;
    private const uint DxgiAdapterFlagSoftware = 2;

    private nint _query;
    private nint _cpuCounter;
    private bool _primed;

    private readonly List<nint> _intelDedicatedMemoryCounters = [];
    private readonly List<nint> _intelSharedMemoryCounters = [];
    private int _intelMemoryRebindAttempts;

    /// <summary>
    /// Opens the PDH query, adds the CPU counter, binds the Intel GPU-memory counters, and does
    /// the initial priming collect. ClawHUD requires one primed collect before a rate counter
    /// yields a real value.
    /// </summary>
    public bool Initialize()
    {
        Reset();

        if (PdhOpenQueryW(null, nint.Zero, out _query) != ErrorSuccess)
        {
            _query = nint.Zero;
            return false;
        }

        if (PdhAddEnglishCounterW(_query, CpuCounterPath, nint.Zero, out _cpuCounter) != ErrorSuccess)
        {
            Reset();
            return false;
        }

        // GPU-memory binding is best-effort; a bounded retry runs from Sample() if it is incomplete.
        TryBindIntelGpuMemoryCounters();

        if (PdhCollectQueryData(_query) != ErrorSuccess)
        {
            Reset();
            return false;
        }

        _primed = false;
        return true;
    }

    /// <summary>
    /// Collects the next sample. The first successful sample after <see cref="Initialize"/>
    /// returns an all-<c>null</c> snapshot (priming) rather than an invented value.
    /// </summary>
    public WindowsUsageSnapshot? Sample()
    {
        if (_query == nint.Zero || PdhCollectQueryData(_query) != ErrorSuccess)
            return null;

        if (ShouldRetryIntelGpuMemoryCounters(
                _intelDedicatedMemoryCounters.Count == 0,
                _intelSharedMemoryCounters.Count == 0,
                _intelMemoryRebindAttempts))
        {
            TryBindIntelGpuMemoryCounters();
        }

        if (!_primed)
        {
            _primed = true;
            return new WindowsUsageSnapshot(null, null, null);
        }

        return new WindowsUsageSnapshot(
            ReadCpuUsagePercent(),
            ReadUsedPhysicalMemoryBytes(),
            CombineGpuMemoryBytes(
                ReadByteCounters(_intelDedicatedMemoryCounters),
                ReadByteCounters(_intelSharedMemoryCounters)));
    }

    public void Dispose() => Reset();

    private double? ReadCpuUsagePercent()
    {
        if (_cpuCounter == nint.Zero)
            return null;

        if (PdhGetFormattedCounterValue(_cpuCounter, PdhFmtDouble, nint.Zero, out var value) != ErrorSuccess ||
            !IsValidCounter(value.CStatus))
        {
            return null;
        }

        return NormalizeUsagePercent(value.DoubleValue);
    }

    private static ulong? ReadUsedPhysicalMemoryBytes()
    {
        var memory = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref memory))
            return null;

        return UsedPhysicalMemory(memory.ullTotalPhys, memory.ullAvailPhys);
    }

    // ---- Intel GPU memory ---------------------------------------------------

    private bool TryBindIntelGpuMemoryCounters()
    {
        var dedicatedEmpty = _intelDedicatedMemoryCounters.Count == 0;
        var sharedEmpty = _intelSharedMemoryCounters.Count == 0;
        if (!ShouldRetryIntelGpuMemoryCounters(dedicatedEmpty, sharedEmpty, _intelMemoryRebindAttempts))
            return !dedicatedEmpty && !sharedEmpty;

        _intelMemoryRebindAttempts++;
        foreach (var counter in _intelDedicatedMemoryCounters)
            PdhRemoveCounter(counter);
        foreach (var counter in _intelSharedMemoryCounters)
            PdhRemoveCounter(counter);
        _intelDedicatedMemoryCounters.Clear();
        _intelSharedMemoryCounters.Clear();

        return AddIntelGpuMemoryCounters();
    }

    private bool AddIntelGpuMemoryCounters()
    {
        if (FindIntelAdapterLuid() is not { } adapterLuid)
            return false;

        var dedicatedPaths = ExpandCounterPaths(DedicatedUsageWildcard);
        var sharedPaths = ExpandCounterPaths(SharedUsageWildcard);
        if (dedicatedPaths is null || sharedPaths is null)
            return false;

        BindMatchingCounters(dedicatedPaths, adapterLuid, _intelDedicatedMemoryCounters);
        BindMatchingCounters(sharedPaths, adapterLuid, _intelSharedMemoryCounters);

        return _intelDedicatedMemoryCounters.Count > 0 && _intelSharedMemoryCounters.Count > 0;
    }

    private void BindMatchingCounters(List<string> paths, Luid adapterLuid, List<nint> target)
    {
        foreach (var path in paths)
        {
            if (!IsIntelGpuMemoryCounterInstance(path, adapterLuid))
                continue;

            if (PdhAddEnglishCounterW(_query, path, nint.Zero, out var counter) == ErrorSuccess)
                target.Add(counter);
        }
    }

    private ulong? ReadByteCounters(List<nint> counters)
    {
        if (counters.Count == 0)
            return null;

        ulong total = 0;
        foreach (var counter in counters)
        {
            if (ReadByteCounter(counter) is not { } value)
                return null;

            if (total > ulong.MaxValue - value)
                return null;

            total += value;
        }

        return total;
    }

    private ulong? ReadByteCounter(nint counter)
    {
        if (counter == nint.Zero)
            return null;

        if (PdhGetFormattedCounterValue(counter, PdhFmtLarge, nint.Zero, out var value) != ErrorSuccess ||
            !IsValidCounter(value.CStatus) ||
            value.LargeValue < 0)
        {
            return null;
        }

        return (ulong)value.LargeValue;
    }

    /// <summary>ClawHUD retry gate: retry only while a category is incomplete, bounded to 3 attempts.</summary>
    internal static bool ShouldRetryIntelGpuMemoryCounters(bool dedicatedEmpty, bool sharedEmpty, int attempts) =>
        (dedicatedEmpty || sharedEmpty) && attempts < MaxIntelMemoryRebindAttempts;

    /// <summary>Both categories required; overflow -&gt; unavailable; a genuine 0 + 0 -&gt; 0.</summary>
    internal static ulong? CombineGpuMemoryBytes(ulong? dedicated, ulong? shared)
    {
        if (dedicated is not { } d || shared is not { } s)
            return null;

        if (d > ulong.MaxValue - s)
            return null;

        return d + s;
    }

    /// <summary>
    /// Parses a <c>\GPU Adapter Memory</c> PDH instance of the form
    /// <c>luid_0x&lt;high&gt;_0x&lt;low&gt;_phys_&lt;n&gt;</c> (zero-padded or compact hex).
    /// Both LUID components must be valid 32-bit hex and a <c>_phys_&lt;n&gt;</c> suffix must
    /// follow. Returns <c>null</c> for any malformed or non-physical instance.
    /// </summary>
    internal static Luid? ParseGpuMemoryInstanceLuid(string instance)
    {
        const string prefix = "luid_";
        const string physicalSuffix = "_phys_";

        var position = instance.IndexOf(prefix, StringComparison.Ordinal);
        if (position < 0)
            return null;

        position += prefix.Length;

        var high = ParseHex32(instance, ref position);
        if (high is null || position >= instance.Length || instance[position++] != '_')
            return null;

        var low = ParseHex32(instance, ref position);
        if (low is null || !instance.AsSpan(position).StartsWith(physicalSuffix))
            return null;

        position += physicalSuffix.Length;
        if (position == instance.Length)
            return null;

        return new Luid(low.Value, (int)high.Value);
    }

    /// <summary>Exact-match: both LUID components must equal the selected Intel adapter's.</summary>
    internal static bool IsIntelGpuMemoryCounterInstance(string instance, Luid adapterLuid) =>
        ParseGpuMemoryInstanceLuid(instance) == adapterLuid;

    private static uint? ParseHex32(string text, ref int position)
    {
        if (position + 2 > text.Length ||
            text[position] != '0' ||
            (text[position + 1] != 'x' && text[position + 1] != 'X'))
        {
            return null;
        }

        position += 2;
        uint result = 0;
        var digits = 0;
        while (position < text.Length)
        {
            var digit = HexDigit(text[position]);
            if (digit < 0)
                break;

            if (digits == 8 || result > (uint.MaxValue - (uint)digit) / 16u)
                return null;

            result = (result * 16u) + (uint)digit;
            position++;
            digits++;
        }

        return digits > 0 ? result : null;
    }

    private static int HexDigit(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => -1,
    };

    private static List<string>? ExpandCounterPaths(string wildcardPath)
    {
        uint length = 0;
        if (PdhExpandWildCardPathW(null, wildcardPath, nint.Zero, ref length, 0) != PdhMoreData || length == 0)
            return null;

        var buffer = Marshal.AllocHGlobal(checked((int)length * sizeof(char)));
        try
        {
            if (PdhExpandWildCardPathW(null, wildcardPath, buffer, ref length, 0) != ErrorSuccess)
                return null;

            var paths = new List<string>();
            var offset = 0;
            while (Marshal.PtrToStringUni(buffer + offset) is { Length: > 0 } path)
            {
                paths.Add(path);
                offset += (path.Length + 1) * sizeof(char);
            }

            return paths;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static Luid? FindIntelAdapterLuid()
    {
        var factoryIid = typeof(IDXGIFactory1).GUID;
        if (CreateDXGIFactory1(ref factoryIid, out var factoryPtr) < 0 || factoryPtr == nint.Zero)
            return null;

        var factory = (IDXGIFactory1)Marshal.GetObjectForIUnknown(factoryPtr);
        Marshal.Release(factoryPtr);
        try
        {
            for (uint index = 0; ; index++)
            {
                if (factory.EnumAdapters1(index, out var adapterPtr) < 0 || adapterPtr == nint.Zero)
                    break;

                var adapter = (IDXGIAdapter1)Marshal.GetObjectForIUnknown(adapterPtr);
                Marshal.Release(adapterPtr);
                try
                {
                    if (adapter.GetDesc1(out var description) >= 0 &&
                        description.VendorId == IntelVendorId &&
                        (description.Flags & DxgiAdapterFlagSoftware) == 0)
                    {
                        return new Luid(description.AdapterLuid.LowPart, description.AdapterLuid.HighPart);
                    }
                }
                finally
                {
                    Marshal.FinalReleaseComObject(adapter);
                }
            }
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(factory);
        }

        return null;
    }

    private void Reset()
    {
        if (_query != nint.Zero)
            PdhCloseQuery(_query);

        _query = nint.Zero;
        _cpuCounter = nint.Zero;
        _primed = false;
        _intelDedicatedMemoryCounters.Clear();
        _intelSharedMemoryCounters.Clear();
        _intelMemoryRebindAttempts = 0;
    }

    private static bool IsValidCounter(uint cStatus) =>
        cStatus == ErrorSuccess || cStatus == PdhCstatusNewData;

    /// <summary>NaN / Infinity / negative -> unavailable; otherwise clamp to 100 (turbo utility).</summary>
    internal static double? NormalizeUsagePercent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
            return null;

        return Math.Min(value, 100.0);
    }

    /// <summary>Used physical bytes = total - available; available &gt; total -> unavailable.</summary>
    internal static ulong? UsedPhysicalMemory(ulong totalBytes, ulong availableBytes)
    {
        if (availableBytes > totalBytes)
            return null;

        return totalBytes - availableBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
        [FieldOffset(8)] public long LargeValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidNative
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public LuidNative AdapterLuid;
        public uint Flags;
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    private interface IDXGIFactory1
    {
        [PreserveSig] int Reserved0();
        [PreserveSig] int Reserved1();
        [PreserveSig] int Reserved2();
        [PreserveSig] int Reserved3();
        [PreserveSig] int Reserved4();
        [PreserveSig] int Reserved5();
        [PreserveSig] int Reserved6();
        [PreserveSig] int Reserved7();
        [PreserveSig] int Reserved8();
        [PreserveSig] int EnumAdapters1(uint index, out nint adapter);
        [PreserveSig] int IsCurrent();
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("29038f61-3839-4626-91fd-086879011a05")]
    private interface IDXGIAdapter1
    {
        [PreserveSig] int Reserved0();
        [PreserveSig] int Reserved1();
        [PreserveSig] int Reserved2();
        [PreserveSig] int Reserved3();
        [PreserveSig] int Reserved4();
        [PreserveSig] int Reserved5();
        [PreserveSig] int Reserved6();
        [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 description);
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out nint factory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? szDataSource, nint dwUserData, out nint phQuery);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(nint hQuery, string szFullCounterPath, nint dwUserData, out nint phCounter);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhExpandWildCardPathW(string? szDataSource, string szWildCardPath, nint mszExpandedPathList, ref uint pcchPathListLength, uint dwFlags);

    [DllImport("pdh.dll")]
    private static extern uint PdhRemoveCounter(nint hCounter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(nint hQuery);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(nint hCounter, uint dwFormat, nint lpdwType, out PDH_FMT_COUNTERVALUE pValue);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(nint hQuery);
}
