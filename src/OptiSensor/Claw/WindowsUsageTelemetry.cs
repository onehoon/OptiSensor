using System.Runtime.InteropServices;

namespace OptiSensor.Claw;

/// <summary>
/// Windows CPU-utility and physical-memory-used telemetry. Direct C# port of ClawHUD's
/// <c>WindowsUsageSampler</c> CPU/RAM path (the Intel GPU-memory counters it also owns are a
/// deferred follow-up PR). <c>null</c> fields mean the value was unavailable for this sample;
/// a genuine 0 remains a value.
/// </summary>
internal sealed record WindowsUsageSnapshot(
    double? CpuUsagePercent,
    ulong? SystemMemoryUsedBytes);

internal sealed class WindowsUsageTelemetryReader : IDisposable
{
    // Exact ClawHUD counter: turbo/utility can exceed 100 and is capped, not rejected.
    private const string CpuCounterPath = @"\Processor Information(_Total)\% Processor Utility";

    private const uint PdhFmtDouble = 0x00000200;
    private const uint ErrorSuccess = 0;
    private const uint PdhCstatusNewData = 0x00000001;

    private nint _query;
    private nint _cpuCounter;
    private bool _primed;

    /// <summary>
    /// Opens the PDH query, adds the CPU counter, and does the initial priming collect.
    /// ClawHUD requires one primed collect before a rate counter yields a real value.
    /// </summary>
    public bool Initialize()
    {
        Reset();

        if (PdhOpenQueryW(null, nint.Zero, out _query) != ErrorSuccess)
        {
            _query = nint.Zero;
            return false;
        }

        if (PdhAddEnglishCounterW(_query, CpuCounterPath, nint.Zero, out _cpuCounter) != ErrorSuccess ||
            PdhCollectQueryData(_query) != ErrorSuccess)
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

        if (!_primed)
        {
            _primed = true;
            return new WindowsUsageSnapshot(null, null);
        }

        return new WindowsUsageSnapshot(ReadCpuUsagePercent(), ReadUsedPhysicalMemoryBytes());
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

    private void Reset()
    {
        if (_query != nint.Zero)
            PdhCloseQuery(_query);

        _query = nint.Zero;
        _cpuCounter = nint.Zero;
        _primed = false;
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
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? szDataSource, nint dwUserData, out nint phQuery);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(nint hQuery, string szFullCounterPath, nint dwUserData, out nint phCounter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(nint hQuery);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(nint hCounter, uint dwFormat, nint lpdwType, out PDH_FMT_COUNTERVALUE pValue);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(nint hQuery);
}
