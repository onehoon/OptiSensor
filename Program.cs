using LibreHardwareMonitor.Hardware;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace OptiSensor;

class Program
{
    static void Main(string[] args)
    {
        using var sensorReader = new SensorReader();
        using var publisher = new ExternalOverlayPublisher();

        sensorReader.Open();
        publisher.Open();

        var showConsoleUpdates = args.Contains("--watch", StringComparer.OrdinalIgnoreCase);
        var runOnce = args.Contains("--once", StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var overlayLine = sensorReader.ReadOverlayLine();

            if (overlayLine is not null)
                publisher.Publish(overlayLine);

            if (showConsoleUpdates || runOnce)
            {
                TryClearConsole();
                Console.WriteLine(overlayLine ?? "No GPU sensor values available.");
            }

            if (runOnce)
                break;

            Thread.Sleep(TimeSpan.FromSeconds(1));
        }
    }

    private static void TryClearConsole()
    {
        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class SensorReader : IDisposable
{
    private readonly Computer _computer = new()
    {
        IsGpuEnabled = true
    };

    public void Open()
    {
        _computer.Open();
    }

    public string? ReadOverlayLine()
    {
        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
                subHardware.Update();
        }

        var gpuSensors = _computer.Hardware
            .Where(IsGpu)
            .SelectMany(GetSensors)
            .ToArray();

        if (gpuSensors.Length == 0)
            return null;

        var temperature = PickSensor(gpuSensors, SensorType.Temperature, "GPU Core", "GPU Temperature", "Core");
        var power = PickSensor(gpuSensors, SensorType.Power, "GPU Package", "GPU Power", "Total");
        var load = PickSensor(gpuSensors, SensorType.Load, "GPU Core", "GPU Load", "Core");

        var parts = new List<string>();

        if (temperature?.Value is float tempValue)
            parts.Add($"GPU {tempValue:0}C");

        if (power?.Value is float powerValue)
            parts.Add($"{powerValue:0}W");

        if (load?.Value is float loadValue)
            parts.Add($"{loadValue:0}%");

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    public void Dispose()
    {
        _computer.Close();
    }

    private static bool IsGpu(IHardware hardware)
    {
        return hardware.HardwareType is HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia;
    }

    private static IEnumerable<ISensor> GetSensors(IHardware hardware)
    {
        foreach (var sensor in hardware.Sensors)
            yield return sensor;

        foreach (var subHardware in hardware.SubHardware)
        {
            foreach (var sensor in subHardware.Sensors)
                yield return sensor;
        }
    }

    private static ISensor? PickSensor(IEnumerable<ISensor> sensors, SensorType type, params string[] preferredNames)
    {
        var typedSensors = sensors
            .Where(sensor => sensor.SensorType == type && sensor.Value.HasValue)
            .ToArray();

        foreach (var preferredName in preferredNames)
        {
            var match = typedSensors.FirstOrDefault(sensor =>
                sensor.Name.Contains(preferredName, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
                return match;
        }

        return typedSensors.FirstOrDefault();
    }
}

internal sealed class ExternalOverlayPublisher : IDisposable
{
    private const string MappingName = "Local\\OptiScalerExternalOverlay";
    private const uint PayloadMagic = 0x564F534F; // OSOV
    private const uint PayloadVersion = 1;
    private const int MaxLines = 4;
    private const int MaxLineLength = 128;
    private const int LastUpdateTickOffset = 16;
    private const int LineCountOffset = 24;
    private const int LinesOffset = 28;
    private const int PayloadSize = 544;

    private MemoryMappedFile? _mappedFile;
    private MemoryMappedViewAccessor? _accessor;
    private uint _sequence;

    public void Open()
    {
        _mappedFile = MemoryMappedFile.CreateOrOpen(MappingName, PayloadSize, MemoryMappedFileAccess.ReadWrite);
        _accessor = _mappedFile.CreateViewAccessor(0, PayloadSize, MemoryMappedFileAccess.ReadWrite);

        _accessor.Write(0, PayloadMagic);
        _accessor.Write(4, PayloadVersion);
        _accessor.Write(8, _sequence);
    }

    public void Publish(string line)
    {
        if (_accessor is null)
            return;

        var bytes = Encoding.ASCII.GetBytes(line);
        var length = Math.Min(bytes.Length, MaxLineLength - 1);
        Span<byte> lineBuffer = stackalloc byte[MaxLineLength];

        bytes.AsSpan(0, length).CopyTo(lineBuffer);

        _sequence++;
        if ((_sequence & 1U) == 0)
            _sequence++;

        _accessor.Write(8, _sequence);
        _accessor.Write(LastUpdateTickOffset, Environment.TickCount64);
        _accessor.Write(LineCountOffset, 1U);
        _accessor.WriteArray(LinesOffset, lineBuffer.ToArray(), 0, lineBuffer.Length);

        _sequence++;
        _accessor.Write(8, _sequence);
        _accessor.Flush();
    }

    public void Dispose()
    {
        _accessor?.Dispose();
        _mappedFile?.Dispose();
    }
}
