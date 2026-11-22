using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace OptiSensor.Native;

internal enum SensorMetricKind
{
    CpuUsage = 1,
    CpuTemperature = 2,
    GpuUsage = 3,
    GpuTemperature = 4,
    GpuPower = 5,
    FanRpm = 6
}

internal enum SensorServiceStatus
{
    Loading = 0,
    Ready = 1,
    Error = 2,
    Stopped = 3
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal unsafe struct OptiSensorReadingAbi
{
    public int Kind;
    public int Available;
    public float Value;
    public fixed byte Identifier[256];
    public fixed byte Device[128];
    public fixed byte Label[128];
    public fixed byte SettingKey[80];
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal unsafe struct OptiSensorSnapshotInfoAbi
{
    public int Status;
    public int Count;
    public int TotalCount;
    public fixed byte Message[256];
}

internal sealed class SensorReading
{
    public required int Kind { get; init; }
    public required int Available { get; init; }
    public required float Value { get; init; }
    public required string Identifier { get; init; }
    public required string Device { get; init; }
    public required string Label { get; init; }
    public required string SettingKey { get; init; }
}

internal readonly struct SensorCandidate(IHardware hardware, ISensor sensor)
{
    public IHardware Hardware { get; } = hardware;
    public ISensor Sensor { get; } = sensor;
}

internal static unsafe class SensorWorker
{
    private const int MaxReadings = 128;
    private static readonly object Gate = new();
    private static SensorReading[] _snapshot = [];
    private static string _message = "Waiting for sensor discovery.";
    private static int _status = (int)SensorServiceStatus.Loading;
    private static int _stopRequested;
    private static Thread? _worker;

    public static int Start()
    {
        lock (Gate)
        {
            if (_worker is { IsAlive: true })
            {
                return 1;
            }

            _stopRequested = 0;
            _snapshot = [];
            _status = (int)SensorServiceStatus.Loading;
            _message = "Opening LibreHardwareMonitor.";

            try
            {
                _worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "OptiSensor hardware polling"
                };
                _worker.Start();
                return 0;
            }
            catch (Exception exception)
            {
                _worker = null;
                _status = (int)SensorServiceStatus.Error;
                _message = Describe(exception);
                return -1;
            }
        }
    }

    public static int Stop()
    {
        lock (Gate)
        {
            if (_worker is not { IsAlive: true })
            {
                _status = (int)SensorServiceStatus.Stopped;
                _message = "Sensor polling stopped.";
                return 0;
            }

            Volatile.Write(ref _stopRequested, 1);
            return 0;
        }
    }

    public static int CopySnapshot(
        OptiSensorReadingAbi* destination,
        int capacity,
        OptiSensorSnapshotInfoAbi* info)
    {
        if (info == null || capacity < 0 || (capacity > 0 && destination == null))
        {
            return -1;
        }

        try
        {
            SensorReading[] snapshot;
            string message;
            int status;
            lock (Gate)
            {
                snapshot = _snapshot;
                message = _message;
                status = _status;
            }

            int count = Math.Min(snapshot.Length, capacity);
            info->Status = status;
            info->Count = count;
            info->TotalCount = snapshot.Length;
            CopyUtf8(message, info->Message, 256);

            for (int index = 0; index < count; index++)
            {
                SensorReading source = snapshot[index];
                OptiSensorReadingAbi* target = &destination[index];
                target->Kind = source.Kind;
                target->Available = source.Available;
                target->Value = source.Value;
                CopyUtf8(source.Identifier, target->Identifier, 256);
                CopyUtf8(source.Device, target->Device, 128);
                CopyUtf8(source.Label, target->Label, 128);
                CopyUtf8(source.SettingKey, target->SettingKey, 80);
            }

            return count;
        }
        catch
        {
            return -2;
        }
    }

    private static void WorkerLoop()
    {
        Computer? computer = null;
        long lastMotherboardUpdate = 0;

        try
        {
            while (Volatile.Read(ref _stopRequested) == 0)
            {
                try
                {
                    if (computer is null)
                    {
                        Publish([], SensorServiceStatus.Loading, "Opening LibreHardwareMonitor.");
                        computer = new Computer
                        {
                            IsBatteryEnabled = false,
                            IsControllerEnabled = false,
                            IsCpuEnabled = true,
                            IsGpuEnabled = true,
                            IsMemoryEnabled = false,
                            IsMotherboardEnabled = true,
                            IsNetworkEnabled = false,
                            IsPowerMonitorEnabled = false,
                            IsPsuEnabled = false,
                            IsStorageEnabled = false
                        };
                        computer.Open();
                        lastMotherboardUpdate = Environment.TickCount64 - 5000;
                    }

                    long now = Environment.TickCount64;
                    bool updateMotherboard = now - lastMotherboardUpdate >= 5000;
                    if (updateMotherboard)
                    {
                        lastMotherboardUpdate = now;
                    }

                    var failedHardware = new HashSet<IHardware>(ReferenceEqualityComparer.Instance);
                    foreach (IHardware root in computer.Hardware)
                    {
                        HardwareType rootType = root.HardwareType;
                        bool cpu = rootType == HardwareType.Cpu;
                        bool gpu = IsGpu(rootType);
                        bool fanSource = cpu || gpu || rootType is HardwareType.Motherboard or HardwareType.SuperIO;
                        if (!fanSource || (rootType is HardwareType.Motherboard or HardwareType.SuperIO) && !updateMotherboard)
                        {
                            continue;
                        }

                        UpdateTree(root, failedHardware);
                    }

                    bool partialFailure = failedHardware.Count > 0;
                    SensorReading[] readings = Collect(computer, failedHardware, ref partialFailure);
                    string message = partialFailure
                        ? "Some hardware did not update; other available sensor readings remain active."
                        : string.Empty;
                    Publish(readings, SensorServiceStatus.Ready, message);
                }
                catch (Exception exception)
                {
                    Publish([], SensorServiceStatus.Error, Describe(exception));
                    try
                    {
                        computer?.Close();
                    }
                    catch
                    {
                    }
                    computer = null;
                    lastMotherboardUpdate = 0;
                }

                Thread.Sleep(1000);
            }
        }
        finally
        {
            try
            {
                computer?.Close();
            }
            catch
            {
            }

            lock (Gate)
            {
                _worker = null;
                _status = (int)SensorServiceStatus.Stopped;
                _message = "Sensor polling stopped.";
            }
        }
    }

    private static void UpdateTree(IHardware hardware, HashSet<IHardware> failedHardware)
    {
        try
        {
            hardware.Update();
        }
        catch
        {
            failedHardware.Add(hardware);
        }

        try
        {
            foreach (IHardware child in hardware.SubHardware)
            {
                UpdateTree(child, failedHardware);
            }
        }
        catch
        {
            failedHardware.Add(hardware);
        }
    }

    private static SensorReading[] Collect(
        Computer computer,
        HashSet<IHardware> failedHardware,
        ref bool partialFailure)
    {
        var readings = new List<SensorReading>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (IHardware root in computer.Hardware)
        {
            HardwareType rootType = root.HardwareType;
            bool cpu = rootType == HardwareType.Cpu;
            bool gpu = IsGpu(rootType);
            bool fanSource = cpu || gpu || rootType is HardwareType.Motherboard or HardwareType.SuperIO;
            if (!fanSource)
            {
                continue;
            }

            List<SensorCandidate> candidates = GetCandidates(root, failedHardware, ref partialFailure);
            if (cpu)
            {
                AddSelected(readings, seen, root, candidates, SensorType.Load,
                    SensorMetricKind.CpuUsage, ["Total", "CPU"], ["Core", "Thread", "Memory"], allowFallback: false);
                AddSelected(readings, seen, root, candidates, SensorType.Temperature,
                    SensorMetricKind.CpuTemperature, ["Package", "Tctl", "Tdie", "CPU"], ["Core", "CCD"], allowFallback: false);
            }
            else if (gpu)
            {
                AddSelected(readings, seen, root, candidates, SensorType.Load,
                    SensorMetricKind.GpuUsage, ["GPU Core", "Core", "GPU", "3D"], ["Memory"], allowFallback: true);
                AddSelected(readings, seen, root, candidates, SensorType.Temperature,
                    SensorMetricKind.GpuTemperature, ["GPU Core", "Core", "GPU"], ["Memory"], allowFallback: true);
                AddSelected(readings, seen, root, candidates, SensorType.Power,
                    SensorMetricKind.GpuPower, ["Board", "Total", "GPU", "Core"], ["Memory"], allowFallback: true);
            }

            foreach (SensorCandidate candidate in candidates)
            {
                if (candidate.Sensor.SensorType == SensorType.Fan)
                {
                    AddReading(readings, seen, root, candidate, SensorMetricKind.FanRpm);
                }
            }
        }

        return readings.ToArray();
    }

    private static List<SensorCandidate> GetCandidates(
        IHardware root,
        HashSet<IHardware> failedHardware,
        ref bool partialFailure)
    {
        var candidates = new List<SensorCandidate>();
        var nodes = new List<IHardware>();
        AddHardwareTree(root, nodes, failedHardware, ref partialFailure);
        foreach (IHardware hardware in nodes)
        {
            if (failedHardware.Contains(hardware))
            {
                continue;
            }

            try
            {
                foreach (ISensor sensor in hardware.Sensors)
                {
                    candidates.Add(new SensorCandidate(hardware, sensor));
                }
            }
            catch
            {
                partialFailure = true;
            }
        }

        candidates.Sort(static (left, right) =>
        {
            int hardwareOrder = StringComparer.Ordinal.Compare(
                left.Hardware.Identifier.ToString(), right.Hardware.Identifier.ToString());
            return hardwareOrder != 0
                ? hardwareOrder
                : StringComparer.Ordinal.Compare(left.Sensor.Identifier.ToString(), right.Sensor.Identifier.ToString());
        });
        return candidates;
    }

    private static void AddHardwareTree(
        IHardware hardware,
        List<IHardware> nodes,
        HashSet<IHardware> failedHardware,
        ref bool partialFailure)
    {
        nodes.Add(hardware);
        try
        {
            foreach (IHardware child in hardware.SubHardware)
            {
                AddHardwareTree(child, nodes, failedHardware, ref partialFailure);
            }
        }
        catch
        {
            failedHardware.Add(hardware);
            partialFailure = true;
        }
    }

    private static void AddSelected(
        List<SensorReading> readings,
        HashSet<string> seen,
        IHardware root,
        List<SensorCandidate> candidates,
        SensorType sensorType,
        SensorMetricKind metricKind,
        string[] preferredNames,
        string[] excludedNames,
        bool allowFallback)
    {
        SensorCandidate? candidate = SelectSensor(candidates, sensorType, preferredNames, excludedNames, allowFallback);
        if (candidate.HasValue)
        {
            AddReading(readings, seen, root, candidate.Value, metricKind);
        }
    }

    private static SensorCandidate? SelectSensor(
        List<SensorCandidate> candidates,
        SensorType sensorType,
        string[] preferredNames,
        string[] excludedNames,
        bool allowFallback)
    {
        foreach (string preferred in preferredNames)
        {
            foreach (SensorCandidate candidate in candidates)
            {
                if (candidate.Sensor.SensorType == sensorType &&
                    candidate.Sensor.Name.Contains(preferred, StringComparison.OrdinalIgnoreCase) &&
                    !ContainsAny(candidate.Sensor.Name, excludedNames))
                {
                    return candidate;
                }
            }
        }

        if (allowFallback)
        {
            foreach (SensorCandidate candidate in candidates)
            {
                if (candidate.Sensor.SensorType == sensorType &&
                    !ContainsAny(candidate.Sensor.Name, excludedNames))
                {
                    return candidate;
                }
            }

            foreach (SensorCandidate candidate in candidates)
            {
                if (candidate.Sensor.SensorType == sensorType)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static bool ContainsAny(string value, string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (value.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddReading(
        List<SensorReading> readings,
        HashSet<string> seen,
        IHardware root,
        SensorCandidate candidate,
        SensorMetricKind kind)
    {
        if (readings.Count >= MaxReadings)
        {
            return;
        }

        string rootIdentity = root.Identifier.ToString();
        string hardwareIdentity = candidate.Hardware.Identifier.ToString();
        string sensorIdentity = candidate.Sensor.Identifier.ToString();
        string identity = string.Concat(rootIdentity, "|", hardwareIdentity, "|", sensorIdentity, "|", (int)kind);
        if (!seen.Add(identity))
        {
            return;
        }

        float? sensorValue = candidate.Sensor.Value;
        bool available = sensorValue.HasValue && float.IsFinite(sensorValue.Value);
        string device = string.Equals(rootIdentity, hardwareIdentity, StringComparison.Ordinal)
            ? root.Name
            : string.Concat(root.Name, " / ", candidate.Hardware.Name);
        string settingKey = "SensorVisible_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));

        readings.Add(new SensorReading
        {
            Kind = (int)kind,
            Available = available ? 1 : 0,
            Value = available ? sensorValue!.Value : 0.0f,
            Identifier = string.Concat(hardwareIdentity, "|", sensorIdentity),
            Device = device,
            Label = candidate.Sensor.Name,
            SettingKey = settingKey
        });
    }

    private static bool IsGpu(HardwareType type) =>
        type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    private static void Publish(SensorReading[] snapshot, SensorServiceStatus status, string message)
    {
        lock (Gate)
        {
            _snapshot = snapshot;
            _status = (int)status;
            _message = message;
        }
    }

    private static void CopyUtf8(string value, byte* destination, int capacity)
    {
        if (capacity <= 0)
        {
            return;
        }

        Span<byte> output = new(destination, capacity);
        output.Clear();
        if (capacity == 1)
        {
            return;
        }

        Encoding.UTF8.GetEncoder().Convert(
            value.AsSpan(), output[..(capacity - 1)], flush: true,
            out _, out int bytesUsed, out _);
        output[bytesUsed] = 0;
    }

    private static string Describe(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : string.Concat(exception.GetType().Name, ": ", exception.Message);
}

internal static unsafe class NativeExports
{
    [UnmanagedCallersOnly(EntryPoint = "OptiSensor_Start", CallConvs = [typeof(CallConvCdecl)])]
    public static int Start()
    {
        try
        {
            return SensorWorker.Start();
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "OptiSensor_GetSnapshot", CallConvs = [typeof(CallConvCdecl)])]
    public static int GetSnapshot(OptiSensorReadingAbi* readings, int capacity, OptiSensorSnapshotInfoAbi* info)
    {
        try
        {
            return SensorWorker.CopySnapshot(readings, capacity, info);
        }
        catch
        {
            return -2;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "OptiSensor_Stop", CallConvs = [typeof(CallConvCdecl)])]
    public static int Stop()
    {
        try
        {
            return SensorWorker.Stop();
        }
        catch
        {
            return -1;
        }
    }

}
