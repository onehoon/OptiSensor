using OptiSensor.Claw;
using Xunit;

namespace OptiSensor.Tests.Claw;

public class MsiEcTelemetryReaderTests
{
    // ---- CPU temperature (Get_Temperature(0), payload[0]) --------------------

    [Theory]
    [InlineData(0x20, 32)]
    [InlineData(0x2C, 44)]
    [InlineData(0x34, 52)]
    public void DecodeCpuTempC_HardwareVectors(byte raw, int expected)
    {
        Assert.Equal(expected, MsiEcTelemetryReader.DecodeCpuTempC(new[] { raw }));
    }

    [Fact]
    public void DecodeCpuTempC_ZeroByteIsUnavailable()
    {
        Assert.Null(MsiEcTelemetryReader.DecodeCpuTempC(new byte[] { 0x00 }));
    }

    [Fact]
    public void DecodeCpuTempC_EmptyPayloadIsUnavailable()
    {
        Assert.Null(MsiEcTelemetryReader.DecodeCpuTempC(ReadOnlySpan<byte>.Empty));
    }

    // ---- Fan RPM (Get_Fan(0), abs(480000 / (a - b))) ------------------------

    [Theory]
    [InlineData(0x00, 0x6F, 4324)]
    [InlineData(0x00, 0x70, 4285)]
    [InlineData(0x00, 0x71, 4247)]
    [InlineData(0x00, 0x72, 4210)]
    [InlineData(0x00, 0x6E, 4363)]
    public void DecodeFanRpm_HardwareVectors(byte a, byte b, int expected)
    {
        Assert.Equal(expected, MsiEcTelemetryReader.DecodeFanRpm(a, b));
    }

    [Fact]
    public void DecodeFanRpm_ZeroDeltaIsStoppedFanNotUnavailable()
    {
        Assert.Equal(0, MsiEcTelemetryReader.DecodeFanRpm(0x70, 0x70));
    }

    [Fact]
    public void TryDecodeFan_TwoFanVector()
    {
        Assert.True(MsiEcTelemetryReader.TryDecodeFan(new byte[] { 0x00, 0x6F, 0x00, 0x6F }, out var fan1, out var fan2));
        Assert.Equal(4324, fan1);
        Assert.Equal(4324, fan2);
    }

    [Fact]
    public void TryDecodeFan_IndependentFanPairs()
    {
        Assert.True(MsiEcTelemetryReader.TryDecodeFan(new byte[] { 0x00, 0x70, 0x00, 0x72 }, out var fan1, out var fan2));
        Assert.Equal(4285, fan1);
        Assert.Equal(4210, fan2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void TryDecodeFan_ShortPayloadIsUnavailable(int length)
    {
        Assert.False(MsiEcTelemetryReader.TryDecodeFan(new byte[length], out _, out _));
    }

    // ---- CPU package power (Get_Data(221), payload[0]) ----------------------

    [Theory]
    [InlineData(0x02, 2)]
    [InlineData(0x07, 7)]
    [InlineData(0x15, 21)]
    [InlineData(0x18, 24)]
    public void DecodeCpuPackagePowerW_HardwareVectors(byte raw, int expected)
    {
        Assert.Equal(expected, MsiEcTelemetryReader.DecodeCpuPackagePowerW(new[] { raw }));
    }

    [Fact]
    public void DecodeCpuPackagePowerW_SuccessfulZeroIsValidZeroWatts()
    {
        Assert.Equal(0, MsiEcTelemetryReader.DecodeCpuPackagePowerW(new byte[] { 0x00 }));
    }

    [Fact]
    public void DecodeCpuPackagePowerW_EmptyPayloadIsUnavailable()
    {
        Assert.Null(MsiEcTelemetryReader.DecodeCpuPackagePowerW(ReadOnlySpan<byte>.Empty));
    }

    // ---- Read flow: one failed metric must not invalidate the others -------

    [Fact]
    public void ReadSnapshot_PartialFailureStillReturnsSuccessfulMetrics()
    {
        var transport = new FakeTransport
        {
            Responses =
            {
                [("Get_Temperature", 0)] = FakeTransport.Ok(0x2C),
                // Get_Fan deliberately absent -> metric-unavailable for that metric only.
                [("Get_Data", 221)] = FakeTransport.Ok(0x15),
            }
        };

        var snapshot = new MsiEcTelemetryReader(transport).ReadSnapshot();

        Assert.Equal(44, snapshot.CpuTempC);
        Assert.Null(snapshot.Fan1Rpm);
        Assert.Null(snapshot.Fan2Rpm);
        Assert.Equal(21, snapshot.CpuPackagePowerW);
    }

    [Fact]
    public void ReadSnapshot_QueriesOnlyTheThreeProductionMetricFamilies()
    {
        var transport = new FakeTransport();

        new MsiEcTelemetryReader(transport).ReadSnapshot();

        Assert.Equal(
            new[] { ("Get_Data", 221), ("Get_Fan", 0), ("Get_Temperature", 0) },
            transport.Calls.OrderBy(call => call.Item1).ToArray());
    }

    [Fact]
    public void ReadSnapshot_AllMetricsAvailable()
    {
        var transport = new FakeTransport
        {
            Responses =
            {
                [("Get_Temperature", 0)] = FakeTransport.Ok(0x34, 0x00),
                [("Get_Fan", 0)] = FakeTransport.Ok(0x00, 0x6F, 0x00, 0x6E),
                [("Get_Data", 221)] = FakeTransport.Ok(0x18),
            }
        };

        var snapshot = new MsiEcTelemetryReader(transport).ReadSnapshot();

        Assert.Equal(52, snapshot.CpuTempC);
        Assert.Equal(4324, snapshot.Fan1Rpm);
        Assert.Equal(4363, snapshot.Fan2Rpm);
        Assert.Equal(24, snapshot.CpuPackagePowerW);
    }

    // ---- shared WMI transport failure bounds the sample -------------------

    [Fact]
    public void ReadSnapshot_FirstReadTransportFailureStopsTheSample()
    {
        var transport = new FakeTransport
        {
            Responses = { [("Get_Temperature", 0)] = (MsiEcReadStatus.TransportUnavailable, []) },
        };

        var snapshot = new MsiEcTelemetryReader(transport).ReadSnapshot();

        Assert.Equal(new[] { ("Get_Temperature", 0) }, transport.Calls);
        Assert.Null(snapshot.CpuTempC);
        Assert.Null(snapshot.Fan1Rpm);
        Assert.Null(snapshot.Fan2Rpm);
        Assert.Null(snapshot.CpuPackagePowerW);
    }

    [Fact]
    public void ReadSnapshot_PartialTelemetrySurvivesLaterTransportFailure()
    {
        var transport = new FakeTransport
        {
            Responses =
            {
                [("Get_Temperature", 0)] = FakeTransport.Ok(0x43), // 67 C
                [("Get_Fan", 0)] = (MsiEcReadStatus.TransportUnavailable, []),
            }
        };

        var snapshot = new MsiEcTelemetryReader(transport).ReadSnapshot();

        Assert.Equal(new[] { ("Get_Temperature", 0), ("Get_Fan", 0) }, transport.Calls); // Get_Data not reached
        Assert.Equal(67, snapshot.CpuTempC);
        Assert.Null(snapshot.Fan1Rpm);
        Assert.Null(snapshot.Fan2Rpm);
        Assert.Null(snapshot.CpuPackagePowerW);
    }

    [Fact]
    public void ReadSnapshot_MetricFailureDoesNotAbortLaterReads()
    {
        var transport = new FakeTransport
        {
            Responses =
            {
                [("Get_Temperature", 0)] = (MsiEcReadStatus.MetricUnavailable, []),
                [("Get_Fan", 0)] = FakeTransport.Ok(0x00, 0x6F, 0x00, 0x6E),
                [("Get_Data", 221)] = FakeTransport.Ok(0x18),
            }
        };

        var snapshot = new MsiEcTelemetryReader(transport).ReadSnapshot();

        Assert.Equal(3, transport.Calls.Count);
        Assert.Null(snapshot.CpuTempC);
        Assert.Equal(4324, snapshot.Fan1Rpm);
        Assert.Equal(4363, snapshot.Fan2Rpm);
        Assert.Equal(24, snapshot.CpuPackagePowerW);
    }

    [Fact]
    public void ReadSnapshot_FanMetricFailureStillAllowsTdp()
    {
        var transport = new FakeTransport
        {
            Responses =
            {
                [("Get_Temperature", 0)] = FakeTransport.Ok(0x34),
                [("Get_Fan", 0)] = (MsiEcReadStatus.MetricUnavailable, []),
                [("Get_Data", 221)] = FakeTransport.Ok(0x18),
            }
        };

        var snapshot = new MsiEcTelemetryReader(transport).ReadSnapshot();

        Assert.Equal(3, transport.Calls.Count);
        Assert.Equal(52, snapshot.CpuTempC);
        Assert.Null(snapshot.Fan1Rpm);
        Assert.Null(snapshot.Fan2Rpm);
        Assert.Equal(24, snapshot.CpuPackagePowerW);
    }

    private sealed class FakeTransport : IMsiEcTransport
    {
        public Dictionary<(string, int), (MsiEcReadStatus Status, byte[] Payload)> Responses { get; } = new();
        public List<(string, int)> Calls { get; } = new();

        /// <summary>Shorthand for a successful read returning <paramref name="payload"/>.</summary>
        public static (MsiEcReadStatus, byte[]) Ok(params byte[] payload) => (MsiEcReadStatus.Success, payload);

        public MsiEcReadStatus Read(string method, int selector, out byte[] payload)
        {
            Calls.Add((method, selector));
            if (Responses.TryGetValue((method, selector), out var response))
            {
                payload = response.Payload;
                return response.Status;
            }

            // Unconfigured metric: unavailable, but not evidence the whole transport is down.
            payload = [];
            return MsiEcReadStatus.MetricUnavailable;
        }
    }
}
