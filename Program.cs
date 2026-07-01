using LibreHardwareMonitor.Hardware;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace OptiSensor;

class Program
{
    static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            SimpleLog.TryWriteException(ex);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (HasArg(args, "--install"))
        {
            AppInstaller.Install(verbose: true);
            return 0;
        }

        if (HasArg(args, "--uninstall"))
        {
            AppInstaller.Uninstall();
            return 0;
        }

        var runOnce = HasArg(args, "--once");
        var watch = HasArg(args, "--watch");
        var startup = HasArg(args, "--startup");

        if (runOnce)
        {
            RunSensorLoop(showConsoleUpdates: true, runOnce: true, publishIntervalMs: 1000);
            return 0;
        }

        if (startup)
        {
            if (AppInstaller.EnsureInstalledAndRelaunchIfNeeded(startup: true))
                return 0;

            using var guard = AcquireSingleInstanceOrExit();
            if (guard is null)
                return 0;

            var settings = AppSettings.LoadOrCreate();
            SimpleLog.TryWrite("Startup execution started.");
            RunSensorLoop(showConsoleUpdates: false, runOnce: false, settings.ClampedPublishIntervalMs);
            return 0;
        }

        if (watch)
        {
            using var guard = AcquireSingleInstanceOrExit();
            if (guard is null)
                return 0;

            var settings = AppSettings.LoadOrCreate();
            RunSensorLoop(showConsoleUpdates: true, runOnce: false, settings.ClampedPublishIntervalMs);
            return 0;
        }

        if (AppInstaller.EnsureInstalledAndRelaunchIfNeeded(startup: false))
            return 0;

        using (var guard = AcquireSingleInstanceOrExit())
        {
            if (guard is null)
                return 0;

            var settings = AppSettings.LoadOrCreate();
            RunSensorLoop(showConsoleUpdates: false, runOnce: false, settings.ClampedPublishIntervalMs);
        }

        return 0;
    }

    private static SingleInstanceGuard? AcquireSingleInstanceOrExit()
    {
        var guard = SingleInstanceGuard.TryAcquire();
        if (guard is null)
        {
            Console.WriteLine("OptiSensor is already running.");
            return null;
        }

        return guard;
    }

    private static bool HasArg(string[] args, string option)
    {
        return args.Contains(option, StringComparer.OrdinalIgnoreCase);
    }

    private static void RunSensorLoop(bool showConsoleUpdates, bool runOnce, int publishIntervalMs)
    {
        using var sensorReader = new SensorReader();
        using var publisher = new ExternalOverlayPublisher();

        sensorReader.Open();
        publisher.Open();

        var delay = TimeSpan.FromMilliseconds(Math.Clamp(publishIntervalMs, 100, 10000));

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

            Thread.Sleep(delay);
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
    private const uint ExpectedPayloadMagic = 0x564F534F;
    private const uint ExpectedPayloadVersion = 1;
    private const int MaxLines = 4;
    private const int MaxLineLength = 128;
    private const int LastUpdateTickOffset = 16;
    private const int LineCountOffset = 24;
    private const int LinesOffset = 28;
    private const int PayloadSize = 544;

    // External overlay protocol v1
    // Offset  Size  Field
    // 0       4     magic = OSOV
    // 4       4     version = 1
    // 8       4     sequence
    // 12      4     padding
    // 16      8     lastUpdateTickMs
    // 24      4     lineCount
    // 28      516   UTF-8 null-terminated lines[4][128]
    // Total: 544 bytes

    private MemoryMappedFile? _mappedFile;
    private MemoryMappedViewAccessor? _accessor;
    private uint _sequence;

    public void Open()
    {
        ValidateProtocolLayout();

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

        var bytes = EncodeUtf8NullTerminatedLine(line);
        Span<byte> lineBuffer = stackalloc byte[MaxLineLength];

        bytes.CopyTo(lineBuffer);

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

    private static byte[] EncodeUtf8NullTerminatedLine(string line)
    {
        var utf8 = Encoding.UTF8;
        var encoder = utf8.GetEncoder();
        var output = new byte[MaxLineLength - 1];

        encoder.Convert(line.AsSpan(), output.AsSpan(), true, out _, out var bytesUsed, out _);
        return output[..bytesUsed];
    }

    private static void ValidateProtocolLayout()
    {
        if (PayloadMagic != ExpectedPayloadMagic)
            throw new InvalidOperationException($"Invalid protocol layout: PayloadMagic=0x{PayloadMagic:X8}, expected 0x{ExpectedPayloadMagic:X8}.");
        if (PayloadVersion != ExpectedPayloadVersion)
            throw new InvalidOperationException($"Invalid protocol layout: PayloadVersion={PayloadVersion}, expected {ExpectedPayloadVersion}.");
        if (LastUpdateTickOffset != 16)
            throw new InvalidOperationException($"Invalid protocol layout: LastUpdateTickOffset={LastUpdateTickOffset}, expected 16.");
        if (LineCountOffset != 24)
            throw new InvalidOperationException($"Invalid protocol layout: LineCountOffset={LineCountOffset}, expected 24.");
        if (LinesOffset != 28)
            throw new InvalidOperationException($"Invalid protocol layout: LinesOffset={LinesOffset}, expected 28.");
        if (PayloadSize != 544)
            throw new InvalidOperationException($"Invalid protocol layout: PayloadSize={PayloadSize}, expected 544.");
    }

    public void Dispose()
    {
        _accessor?.Dispose();
        _mappedFile?.Dispose();
    }
}
