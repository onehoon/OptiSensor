using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace OptiSensor.Overlay;

/// <summary>
/// Reads back <c>Local\OptiScalerExternalOverlay</c> the same way the OptiScaler consumer does, so
/// the UI preview shows what is actually present and consumable rather than what OptiSensor
/// intended to publish. Diagnostic/UI only: an unavailable, invalid, or stale mapping simply
/// yields <c>null</c> - never an exception surfaced to the caller, and never any effect on the
/// publisher.
/// </summary>
internal sealed class ExternalOverlayReader : IDisposable
{
    private readonly byte[] _buffer = new byte[ExternalOverlayProtocol.PayloadSize];
    private MemoryMappedFile? _mappedFile;
    private MemoryMappedViewAccessor? _accessor;

    /// <summary>
    /// The current visible line, or <c>null</c> if shared memory is unavailable / invalid / stale.
    /// </summary>
    public string? TryReadLine()
    {
        try
        {
            if (_accessor is null && !TryOpen())
                return null;

            var accessor = _accessor!;

            // Seqlock: read the sequence, then the payload, then the sequence again. A stable,
            // even sequence across both reads means we saw a complete, not-being-written record.
            var sequenceBefore = accessor.ReadUInt32(ExternalOverlayProtocol.SequenceOffset);
            if ((sequenceBefore & 1u) != 0)
                return null;

            Interlocked.MemoryBarrier();

            var magic = accessor.ReadUInt32(ExternalOverlayProtocol.MagicOffset);
            var version = accessor.ReadUInt32(ExternalOverlayProtocol.VersionOffset);
            var lastUpdateTickMs = accessor.ReadInt64(ExternalOverlayProtocol.LastUpdateTickOffset);
            var lineCount = accessor.ReadUInt32(ExternalOverlayProtocol.LineCountOffset);
            accessor.ReadArray(ExternalOverlayProtocol.LinesOffset, _buffer, 0, ExternalOverlayProtocol.MaxLineLength);

            Interlocked.MemoryBarrier();

            var sequenceAfter = accessor.ReadUInt32(ExternalOverlayProtocol.SequenceOffset);
            if (sequenceAfter != sequenceBefore || (sequenceAfter & 1u) != 0)
                return null;

            if (magic != ExternalOverlayProtocol.PayloadMagic || version != ExternalOverlayProtocol.PayloadVersion)
                return null;

            if (lineCount < 1 || lineCount > ExternalOverlayProtocol.MaxLines)
                return null;

            var ageMs = Environment.TickCount64 - lastUpdateTickMs;
            if (ageMs < 0 || ageMs > ExternalOverlayProtocol.StaleAfterMs)
                return null;

            return DecodeLine(_buffer);
        }
        catch (Exception)
        {
            // Mapping may have been torn down between calls; drop it and retry on the next tick.
            DisposeMapping();
            return null;
        }
    }

    private bool TryOpen()
    {
        try
        {
            _mappedFile = MemoryMappedFile.OpenExisting(ExternalOverlayProtocol.MappingName, MemoryMappedFileRights.Read);
            _accessor = _mappedFile.CreateViewAccessor(0, ExternalOverlayProtocol.PayloadSize, MemoryMappedFileAccess.Read);
            return true;
        }
        catch (Exception)
        {
            DisposeMapping();
            return false;
        }
    }

    private static string? DecodeLine(byte[] payload)
    {
        var length = Array.IndexOf<byte>(payload, 0, 0, ExternalOverlayProtocol.MaxLineLength);
        if (length <= 0)
            return null;

        try
        {
            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(payload, 0, length);
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private void DisposeMapping()
    {
        _accessor?.Dispose();
        _mappedFile?.Dispose();
        _accessor = null;
        _mappedFile = null;
    }

    public void Dispose() => DisposeMapping();
}
