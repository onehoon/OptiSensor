using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace OptiSensor.Native;

internal enum SensorGroupDefault
{
    Collapsed = 0,
    Expanded = 1
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
    public int GroupDefaultExpanded;
    public int Available;
    public float Value;
    public fixed byte Identifier[256];
    public fixed byte GroupIdentifier[256];
    public fixed byte Device[128];
    public fixed byte Label[128];
    public fixed byte SensorType[32];
    public fixed byte Unit[32];
    public fixed byte SettingKey[80];
    public fixed byte GroupSettingKey[80];
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
    public required int GroupDefaultExpanded { get; init; }
    public required int Available { get; init; }
    public required float Value { get; init; }
    public required string Identifier { get; init; }
    public required string GroupIdentifier { get; init; }
    public required string Device { get; init; }
    public required string Label { get; init; }
    public required string SensorType { get; init; }
    public required string Unit { get; init; }
    public required string SettingKey { get; init; }
    public required string GroupSettingKey { get; init; }
}

internal readonly struct SensorCandidate(IHardware root, IHardware hardware, ISensor sensor)
{
    public IHardware Root { get; } = root;
    public IHardware Hardware { get; } = hardware;
    public ISensor Sensor { get; } = sensor;
}

internal static unsafe class SensorWorker
{
    private const int MaxReadings = 512;
    private const int SlowHardwareUpdateMilliseconds = 5000;
    private static readonly object Gate = new();
    private static SensorReading[] _snapshot = [];
    private static int _totalSensorCount;
    private static string _message = "Waiting for sensor discovery.";
    private static int _status = (int)SensorServiceStatus.Loading;
    private static int _stopRequested;
    private static int _gpuMonitoringEnabled = 1;
    private static Thread? _worker;

    public static int Start(int enableGpuMonitoring)
    {
        lock (Gate)
        {
            if (_worker is { IsAlive: true })
            {
                return 1;
            }

            Volatile.Write(ref _gpuMonitoringEnabled, enableGpuMonitoring != 0 ? 1 : 0);
            _stopRequested = 0;
            _snapshot = [];
            _totalSensorCount = 0;
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
            int totalSensorCount;
            lock (Gate)
            {
                snapshot = _snapshot;
                totalSensorCount = _totalSensorCount;
                message = _message;
                status = _status;
            }

            int count = Math.Min(snapshot.Length, capacity);
            info->Status = status;
            info->Count = count;
            info->TotalCount = totalSensorCount;
            CopyUtf8(message, info->Message, 256);

            for (int index = 0; index < count; index++)
            {
                SensorReading source = snapshot[index];
                OptiSensorReadingAbi* target = &destination[index];
                target->GroupDefaultExpanded = source.GroupDefaultExpanded;
                target->Available = source.Available;
                target->Value = source.Value;
                CopyUtf8(source.Identifier, target->Identifier, 256);
                CopyUtf8(source.GroupIdentifier, target->GroupIdentifier, 256);
                CopyUtf8(source.Device, target->Device, 128);
                CopyUtf8(source.Label, target->Label, 128);
                CopyUtf8(source.SensorType, target->SensorType, 32);
                CopyUtf8(source.Unit, target->Unit, 32);
                CopyUtf8(source.SettingKey, target->SettingKey, 80);
                CopyUtf8(source.GroupSettingKey, target->GroupSettingKey, 80);
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
        long lastSlowHardwareUpdate = 0;

        try
        {
            while (Volatile.Read(ref _stopRequested) == 0)
            {
                try
                {
                    if (computer is null)
                    {
                        Publish([], 0, SensorServiceStatus.Loading, "Opening LibreHardwareMonitor.");
                        computer = new Computer
                        {
                            IsBatteryEnabled = true,
                            IsControllerEnabled = true,
                            IsCpuEnabled = true,
                            IsGpuEnabled = Volatile.Read(ref _gpuMonitoringEnabled) != 0,
                            IsMemoryEnabled = true,
                            IsMotherboardEnabled = true,
                            IsNetworkEnabled = false,
                            IsPowerMonitorEnabled = true,
                            IsPsuEnabled = true,
                            IsStorageEnabled = false
                        };
                        computer.Open();
                        lastSlowHardwareUpdate = Environment.TickCount64 - SlowHardwareUpdateMilliseconds;
                    }

                    long now = Environment.TickCount64;
                    bool updateSlowHardware = now - lastSlowHardwareUpdate >= SlowHardwareUpdateMilliseconds;
                    if (updateSlowHardware)
                    {
                        lastSlowHardwareUpdate = now;
                    }

                    var failedHardware = new HashSet<IHardware>(ReferenceEqualityComparer.Instance);
                    foreach (IHardware root in computer.Hardware)
                    {
                        HardwareType rootType = root.HardwareType;
                        if (IsExcludedHardware(rootType))
                        {
                            continue;
                        }

                        bool fastHardware = rootType == HardwareType.Cpu || IsGpu(rootType);
                        if (!fastHardware && !updateSlowHardware)
                        {
                            continue;
                        }

                        UpdateTree(root, failedHardware);
                    }

                    bool partialFailure = failedHardware.Count > 0;
                    SensorReading[] readings = Collect(computer, failedHardware, ref partialFailure, out int totalSensorCount);
                    string message = partialFailure
                        ? "Some hardware did not update; other available sensor readings remain active."
                        : string.Empty;
                    if (totalSensorCount > readings.Length)
                    {
                        message = string.IsNullOrEmpty(message)
                            ? "Sensor snapshot limit reached."
                            : string.Concat(message, " Sensor snapshot limit reached.");
                    }
                    Publish(readings, totalSensorCount, SensorServiceStatus.Ready, message);
                }
                catch (Exception exception)
                {
                    Publish([], 0, SensorServiceStatus.Error, Describe(exception));
                    try
                    {
                        computer?.Close();
                    }
                    catch
                    {
                    }
                    computer = null;
                    lastSlowHardwareUpdate = 0;
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
                _snapshot = [];
                _totalSensorCount = 0;
                _status = (int)SensorServiceStatus.Stopped;
                _message = "Sensor polling stopped.";
            }
        }
    }

    private static void UpdateTree(IHardware hardware, HashSet<IHardware> failedHardware)
    {
        if (IsExcludedHardware(hardware.HardwareType))
        {
            return;
        }

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
        ref bool partialFailure,
        out int totalSensorCount)
    {
        var candidates = new List<SensorCandidate>();
        IHardware[] roots = computer.Hardware
            .Where(root => !IsExcludedHardware(root.HardwareType))
            .OrderBy(root => root.Identifier.ToString(), StringComparer.Ordinal)
            .ToArray();

        foreach (IHardware root in roots)
        {
            candidates.AddRange(GetCandidates(root, ref partialFailure));
        }

        candidates.Sort(static (left, right) =>
        {
            int groupOrder = StringComparer.Ordinal.Compare(
                left.Root.Identifier.ToString(), right.Root.Identifier.ToString());
            if (groupOrder != 0)
            {
                return groupOrder;
            }

            int hardwareOrder = StringComparer.Ordinal.Compare(
                left.Hardware.Identifier.ToString(), right.Hardware.Identifier.ToString());
            return hardwareOrder != 0
                ? hardwareOrder
                : StringComparer.Ordinal.Compare(left.Sensor.Identifier.ToString(), right.Sensor.Identifier.ToString());
        });

        var readings = new List<SensorReading>(Math.Min(candidates.Count, MaxReadings));
        foreach (SensorCandidate candidate in candidates)
        {
            try
            {
                readings.Add(CreateReading(candidate, ref partialFailure));
            }
            catch
            {
                partialFailure = true;
            }
        }

        totalSensorCount = readings.Count;
        return readings.Count <= MaxReadings
            ? readings.ToArray()
            : readings.GetRange(0, MaxReadings).ToArray();
    }

    private static List<SensorCandidate> GetCandidates(IHardware root, ref bool partialFailure)
    {
        var candidates = new List<SensorCandidate>();
        var nodes = new List<IHardware>();
        AddHardwareTree(root, nodes, ref partialFailure);
        foreach (IHardware hardware in nodes)
        {
            try
            {
                foreach (ISensor sensor in hardware.Sensors)
                {
                    candidates.Add(new SensorCandidate(root, hardware, sensor));
                }
            }
            catch
            {
                partialFailure = true;
            }
        }

        return candidates;
    }

    private static void AddHardwareTree(
        IHardware hardware,
        List<IHardware> nodes,
        ref bool partialFailure)
    {
        if (IsExcludedHardware(hardware.HardwareType))
        {
            return;
        }

        nodes.Add(hardware);
        try
        {
            foreach (IHardware child in hardware.SubHardware)
            {
                AddHardwareTree(child, nodes, ref partialFailure);
            }
        }
        catch
        {
            partialFailure = true;
        }
    }

    private static SensorReading CreateReading(SensorCandidate candidate, ref bool partialFailure)
    {
        string rootIdentity = candidate.Root.Identifier.ToString();
        string hardwareIdentity = candidate.Hardware.Identifier.ToString();
        string sensorIdentity = candidate.Sensor.Identifier.ToString();
        string identity = string.Concat(rootIdentity, "|", hardwareIdentity, "|", sensorIdentity);
        string sensorLabel = string.Equals(rootIdentity, hardwareIdentity, StringComparison.Ordinal)
            ? candidate.Sensor.Name
            : string.Concat(candidate.Hardware.Name, " / ", candidate.Sensor.Name);
        SensorType sensorType = candidate.Sensor.SensorType;
        float? sensorValue;
        try
        {
            sensorValue = candidate.Sensor.Value;
        }
        catch
        {
            sensorValue = null;
            partialFailure = true;
        }

        bool available = sensorValue.HasValue && float.IsFinite(sensorValue.Value);
        float displayValue = available ? NormalizeValue(sensorType, sensorValue!.Value) : 0.0f;
        string groupSettingKey = CreateSettingKey("GroupExpanded_", rootIdentity);
        return new SensorReading
        {
            GroupDefaultExpanded = GetGroupDefaultExpanded(candidate.Root.HardwareType),
            Available = available ? 1 : 0,
            Value = displayValue,
            Identifier = identity,
            GroupIdentifier = rootIdentity,
            Device = GetGroupTitle(candidate.Root),
            Label = sensorLabel,
            SensorType = sensorType.ToString(),
            Unit = GetUnit(sensorType),
            SettingKey = CreateSettingKey("SensorVisible_", identity),
            GroupSettingKey = groupSettingKey
        };
    }

    private static string CreateSettingKey(string prefix, string identifier) =>
        prefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifier)));

    private static string GetGroupTitle(IHardware hardware)
    {
        string category = hardware.HardwareType switch
        {
            HardwareType.Cpu => "CPU",
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "GPU",
            HardwareType.Memory => "Memory",
            HardwareType.Motherboard or HardwareType.SuperIO => "Motherboard / SuperIO",
            HardwareType.Battery => "Battery",
            HardwareType.EmbeddedController => "Controller",
            HardwareType.Psu or HardwareType.PowerMonitor => "PSU / Power Monitor",
            _ => hardware.HardwareType.ToString()
        };

        return string.Concat(category, " — ", hardware.Name);
    }

    private static int GetGroupDefaultExpanded(HardwareType type) =>
        type == HardwareType.Cpu || IsGpu(type)
            ? (int)SensorGroupDefault.Expanded
            : (int)SensorGroupDefault.Collapsed;

    private static float NormalizeValue(SensorType type, float value) => type switch
    {
        SensorType.Throughput => value / (1024f * 1024f),
        _ => value
    };

    private static string GetUnit(SensorType type) => type.ToString() switch
    {
        "Voltage" => "V",
        "Current" => "A",
        "Power" => "W",
        "Clock" => "MHz",
        "Frequency" => "Hz",
        "Temperature" => "\u00B0C",
        "Load" or "Control" or "Level" or "Humidity" => "%",
        "Fan" => "RPM",
        "Flow" => "L/h",
        "Factor" => "x",
        "Data" => "GB",
        "SmallData" => "MB",
        "Throughput" => "MB/s",
        "TimeSpan" => "s",
        "Timing" => "ns",
        "Energy" => "mWh",
        "Noise" => "dB(A)",
        "Conductivity" => "\u00B5S/cm",
        _ => string.Empty
    };

    private static bool IsGpu(HardwareType type) =>
        type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    private static bool IsExcludedHardware(HardwareType type) =>
        type is HardwareType.Storage or HardwareType.Network;

    private static void Publish(
        SensorReading[] snapshot,
        int totalSensorCount,
        SensorServiceStatus status,
        string message)
    {
        lock (Gate)
        {
            _snapshot = snapshot;
            _totalSensorCount = totalSensorCount;
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
    public static int Start(int enableGpuMonitoring)
    {
        try
        {
            return SensorWorker.Start(enableGpuMonitoring);
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
