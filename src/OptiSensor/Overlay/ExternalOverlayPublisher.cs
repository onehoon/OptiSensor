using System.IO.MemoryMappedFiles;
using System.Text;

namespace OptiSensor.Overlay;

internal sealed class ExternalOverlayPublisher : IDisposable
{
    private MemoryMappedFile? _mappedFile;
    private MemoryMappedViewAccessor? _accessor;
    private uint _sequence;

    public void Open()
    {
        ValidateProtocolLayout();

        _mappedFile = MemoryMappedFile.CreateOrOpen(ExternalOverlayProtocol.MappingName, ExternalOverlayProtocol.PayloadSize, MemoryMappedFileAccess.ReadWrite);
        _accessor = _mappedFile.CreateViewAccessor(0, ExternalOverlayProtocol.PayloadSize, MemoryMappedFileAccess.ReadWrite);

        _accessor.Write(0, ExternalOverlayProtocol.PayloadMagic);
        _accessor.Write(4, ExternalOverlayProtocol.PayloadVersion);
        _accessor.Write(8, _sequence);
    }

    public void Publish(string line)
    {
        if (_accessor is null)
            return;

        var bytes = EncodeUtf8Line(line);
        Span<byte> lineBuffer = stackalloc byte[ExternalOverlayProtocol.MaxLineLength];
        lineBuffer.Clear();
        bytes.CopyTo(lineBuffer);

        _sequence++;
        if ((_sequence & 1U) == 0)
            _sequence++;

        _accessor.Write(8, _sequence);
        _accessor.Write(ExternalOverlayProtocol.LastUpdateTickOffset, Environment.TickCount64);
        _accessor.Write(ExternalOverlayProtocol.LineCountOffset, 1U);
        _accessor.WriteArray(ExternalOverlayProtocol.LinesOffset, lineBuffer.ToArray(), 0, lineBuffer.Length);

        _sequence++;
        _accessor.Write(8, _sequence);
        _accessor.Flush();
    }

    public void Clear()
    {
        if (_accessor is null)
            return;

        _sequence++;
        if ((_sequence & 1U) == 0)
            _sequence++;

        _accessor.Write(8, _sequence);
        _accessor.Write(ExternalOverlayProtocol.LastUpdateTickOffset, Environment.TickCount64);
        _accessor.Write(ExternalOverlayProtocol.LineCountOffset, 0U);

        var lineBuffer = new byte[ExternalOverlayProtocol.MaxLines * ExternalOverlayProtocol.MaxLineLength];
        _accessor.WriteArray(ExternalOverlayProtocol.LinesOffset, lineBuffer, 0, lineBuffer.Length);

        _sequence++;
        _accessor.Write(8, _sequence);
        _accessor.Flush();
    }

    private static byte[] EncodeUtf8Line(string line)
    {
        var utf8 = Encoding.UTF8;
        var encoder = utf8.GetEncoder();
        var output = new byte[ExternalOverlayProtocol.MaxLineLength - 1];

        encoder.Convert(line.AsSpan(), output.AsSpan(), true, out _, out var bytesUsed, out _);
        return output[..bytesUsed];
    }

    private static void ValidateProtocolLayout()
    {
        if (ExternalOverlayProtocol.PayloadMagic != 0x564F534F)
            throw new InvalidOperationException($"Invalid protocol layout: PayloadMagic=0x{ExternalOverlayProtocol.PayloadMagic:X8}, expected 0x564F534F.");
        if (ExternalOverlayProtocol.PayloadVersion != 1)
            throw new InvalidOperationException($"Invalid protocol layout: PayloadVersion={ExternalOverlayProtocol.PayloadVersion}, expected 1.");
        if (ExternalOverlayProtocol.LastUpdateTickOffset != 16)
            throw new InvalidOperationException($"Invalid protocol layout: LastUpdateTickOffset={ExternalOverlayProtocol.LastUpdateTickOffset}, expected 16.");
        if (ExternalOverlayProtocol.LineCountOffset != 24)
            throw new InvalidOperationException($"Invalid protocol layout: LineCountOffset={ExternalOverlayProtocol.LineCountOffset}, expected 24.");
        if (ExternalOverlayProtocol.LinesOffset != 28)
            throw new InvalidOperationException($"Invalid protocol layout: LinesOffset={ExternalOverlayProtocol.LinesOffset}, expected 28.");
        if (ExternalOverlayProtocol.PayloadSize != 544)
            throw new InvalidOperationException($"Invalid protocol layout: PayloadSize={ExternalOverlayProtocol.PayloadSize}, expected 544.");
    }

    public void Dispose()
    {
        _accessor?.Dispose();
        _mappedFile?.Dispose();
    }
}
