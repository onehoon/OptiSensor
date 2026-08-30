namespace OptiSensor.Claw;

/// <summary>
/// Reads the three hardware-validated MSI EC production metric families
/// (<c>Get_Temperature(0)</c>, <c>Get_Fan(0)</c>, <c>Get_Data(221)</c>) and decodes them
/// per ClawHUD's parsing contract. Each read is independent: one failed metric never
/// invalidates the others in the same sample. The one exception is a read that reports
/// <see cref="MsiEcReadStatus.TransportUnavailable"/> - the remaining reads would just repeat the
/// same shared-WMI failure, so they are skipped and retried on the next Core sample.
/// </summary>
internal sealed class MsiEcTelemetryReader
{
    private const string GetTemperature = "Get_Temperature";
    private const string GetFan = "Get_Fan";
    private const string GetData = "Get_Data";
    private const int CpuPackagePowerSelector = 221;

    private readonly IMsiEcTransport _transport;

    public MsiEcTelemetryReader() : this(new MsiEcWmiTransport()) { }

    public MsiEcTelemetryReader(IMsiEcTransport transport) => _transport = transport;

    public MsiEcTelemetrySnapshot ReadSnapshot()
    {
        int? cpuTempC = null;
        int? fan1Rpm = null;
        int? fan2Rpm = null;
        int? cpuPackagePowerW = null;

        MsiEcTelemetrySnapshot Collected() => new(cpuTempC, fan1Rpm, fan2Rpm, cpuPackagePowerW);

        var status = _transport.Read(GetTemperature, 0, out var temperature);
        if (status == MsiEcReadStatus.Success)
            cpuTempC = DecodeCpuTempC(temperature);
        else if (status == MsiEcReadStatus.TransportUnavailable)
            return Collected();

        status = _transport.Read(GetFan, 0, out var fan);
        if (status == MsiEcReadStatus.Success && TryDecodeFan(fan, out var decodedFan1, out var decodedFan2))
        {
            fan1Rpm = decodedFan1;
            fan2Rpm = decodedFan2;
        }
        else if (status == MsiEcReadStatus.TransportUnavailable)
        {
            return Collected();
        }

        status = _transport.Read(GetData, CpuPackagePowerSelector, out var power);
        if (status == MsiEcReadStatus.Success)
            cpuPackagePowerW = DecodeCpuPackagePowerW(power);

        return Collected();
    }

    /// <summary><c>Get_Temperature(0)</c> payload[0] = CPU °C. Empty payload or a 0 byte is unavailable.</summary>
    internal static int? DecodeCpuTempC(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            return null;

        int value = payload[0];
        return value == 0 ? null : value;
    }

    /// <summary>
    /// <c>Get_Fan(0)</c>: payload[0],[1] = Fan 1 tach pair, payload[2],[3] = Fan 2 tach pair.
    /// Needs at least 4 bytes; a decoded pair is always a value (0 RPM for a stopped fan).
    /// </summary>
    internal static bool TryDecodeFan(ReadOnlySpan<byte> payload, out int fan1Rpm, out int fan2Rpm)
    {
        fan1Rpm = 0;
        fan2Rpm = 0;
        if (payload.Length < 4)
            return false;

        fan1Rpm = DecodeFanRpm(payload[0], payload[1]);
        fan2Rpm = DecodeFanRpm(payload[2], payload[3]);
        return true;
    }

    /// <summary>RPM = abs(480000 / (a - b)); a signed delta of 0 is a valid stopped fan (0 RPM).</summary>
    internal static int DecodeFanRpm(byte a, byte b)
    {
        int delta = a - b;
        if (delta == 0)
            return 0;

        return Math.Abs(480000 / delta);
    }

    /// <summary>
    /// <c>Get_Data(221)</c> payload[0] = current CPU package power in watts (not a configured
    /// PL1/PL2 limit). Empty payload is unavailable; a successful 0 is a valid 0 W.
    /// </summary>
    internal static int? DecodeCpuPackagePowerW(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            return null;

        return payload[0];
    }
}
