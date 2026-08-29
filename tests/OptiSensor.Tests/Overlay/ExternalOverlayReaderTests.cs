using System.IO.MemoryMappedFiles;
using System.Text;
using OptiSensor.Overlay;
using Xunit;

namespace OptiSensor.Tests.Overlay;

/// <summary>
/// Shares a collection with the live-publisher test so the two don't contend on the single
/// well-known mapping name in parallel.
/// </summary>
[Collection("ExternalOverlayMapping")]
public sealed class ExternalOverlayReaderTests : IDisposable
{
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _accessor;

    public ExternalOverlayReaderTests()
    {
        _mapping = MemoryMappedFile.CreateOrOpen(ExternalOverlayProtocol.MappingName, ExternalOverlayProtocol.PayloadSize);
        _accessor = _mapping.CreateViewAccessor(0, ExternalOverlayProtocol.PayloadSize, MemoryMappedFileAccess.ReadWrite);
        for (var i = 0; i < ExternalOverlayProtocol.PayloadSize; i += 8)
            _accessor.Write(i, 0L);
    }

    private void WritePayload(
        uint magic = ExternalOverlayProtocol.PayloadMagic,
        uint version = ExternalOverlayProtocol.PayloadVersion,
        uint sequence = 2,
        long? lastUpdateTickMs = null,
        uint lineCount = 1,
        string line = "CPU 36% 67°C | GPU 98% 2300MHz")
    {
        _accessor.Write(ExternalOverlayProtocol.MagicOffset, magic);
        _accessor.Write(ExternalOverlayProtocol.VersionOffset, version);
        _accessor.Write(ExternalOverlayProtocol.SequenceOffset, sequence);
        _accessor.Write(ExternalOverlayProtocol.LastUpdateTickOffset, lastUpdateTickMs ?? Environment.TickCount64);
        _accessor.Write(ExternalOverlayProtocol.LineCountOffset, lineCount);

        var buffer = new byte[ExternalOverlayProtocol.MaxLineLength];
        var bytes = Encoding.UTF8.GetBytes(line);
        Array.Copy(bytes, buffer, Math.Min(bytes.Length, buffer.Length - 1));
        _accessor.WriteArray(ExternalOverlayProtocol.LinesOffset, buffer, 0, buffer.Length);
    }

    private static string? Read()
    {
        using var reader = new ExternalOverlayReader();
        return reader.TryReadLine();
    }

    [Fact]
    public void ValidPayload_ReturnsFirstLine()
    {
        WritePayload();
        Assert.Equal("CPU 36% 67°C | GPU 98% 2300MHz", Read());
    }

    [Fact]
    public void UninitializedPayload_ReturnsNull()
    {
        // Constructor zeroed the region: magic is 0.
        Assert.Null(Read());
    }

    [Fact]
    public void WrongMagic_ReturnsNull()
    {
        WritePayload(magic: 0xDEADBEEF);
        Assert.Null(Read());
    }

    [Fact]
    public void WrongVersion_ReturnsNull()
    {
        WritePayload(version: 2);
        Assert.Null(Read());
    }

    [Fact]
    public void OddSequence_MeansBeingWritten_ReturnsNull()
    {
        WritePayload(sequence: 3);
        Assert.Null(Read());
    }

    [Fact]
    public void ZeroLineCount_ReturnsNull()
    {
        WritePayload(lineCount: 0);
        Assert.Null(Read());
    }

    [Fact]
    public void LineCountAboveMax_ReturnsNull()
    {
        WritePayload(lineCount: (uint)ExternalOverlayProtocol.MaxLines + 1);
        Assert.Null(Read());
    }

    [Fact]
    public void FutureTimestamp_ReturnsNull()
    {
        WritePayload(lastUpdateTickMs: Environment.TickCount64 + 10_000);
        Assert.Null(Read());
    }

    [Fact]
    public void OlderThanFreshnessWindow_ReturnsNull()
    {
        WritePayload(lastUpdateTickMs: Environment.TickCount64 - (ExternalOverlayProtocol.StaleAfterMs + 500));
        Assert.Null(Read());
    }

    [Fact]
    public void WithinFreshnessWindow_IsReadable()
    {
        WritePayload(lastUpdateTickMs: Environment.TickCount64 - (ExternalOverlayProtocol.StaleAfterMs - 250));
        Assert.Equal("CPU 36% 67°C | GPU 98% 2300MHz", Read());
    }

    [Fact]
    public void EmptyLine_ReturnsNull()
    {
        WritePayload(line: string.Empty);
        Assert.Null(Read());
    }

    [Fact]
    public void MultiByteUtf8_RoundTripsExactly()
    {
        WritePayload(line: "FAN 3540RPM | BAT 72% 2.5h | °C");
        Assert.Equal("FAN 3540RPM | BAT 72% 2.5h | °C", Read());
    }

    public void Dispose()
    {
        _accessor.Dispose();
        _mapping.Dispose();
    }
}

[CollectionDefinition("ExternalOverlayMapping", DisableParallelization = true)]
public sealed class ExternalOverlayMappingCollection;
