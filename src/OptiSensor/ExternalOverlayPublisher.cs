using System.IO.MemoryMappedFiles;
using System.Text;

namespace OptiSensor;

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
    // 28      512   UTF-8 null-terminated lines[4][128]
    // 540     4     trailing padding
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
