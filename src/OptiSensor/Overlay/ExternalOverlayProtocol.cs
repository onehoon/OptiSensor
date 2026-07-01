namespace OptiSensor.Overlay;

internal static class ExternalOverlayProtocol
{
    public const string MappingName = "Local\\OptiScalerExternalOverlay";
    public const uint PayloadMagic = 0x564F534F; // OSOV
    public const uint PayloadVersion = 1;
    public const int MaxLines = 4;
    public const int MaxLineLength = 128;
    public const int LastUpdateTickOffset = 16;
    public const int LineCountOffset = 24;
    public const int LinesOffset = 28;
    public const int PayloadSize = 544;

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
}
