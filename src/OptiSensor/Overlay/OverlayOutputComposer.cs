using OptiSensor.Libre;
using OptiSensor.Models;

namespace OptiSensor.Overlay;

internal sealed record OverlayOutputComposition(
    string? Line,
    int EnabledSelectedSensorCount,
    int TotalSelectedSensorCount,
    bool UsedDefaultSelection);

/// <summary>
/// The single output-selection rule shared by the real publisher and the UI preview:
/// given the same sensor snapshot and overlay group snapshot, always produces the same
/// nullable overlay line. Pure composition only - no sensor caching, no shared-memory
/// I/O, no UI text, and no mutation of its inputs.
/// </summary>
internal sealed class OverlayOutputComposer
{
    private readonly OverlayLineBuilder _lineBuilder;

    public OverlayOutputComposer(OverlayLineBuilder lineBuilder)
    {
        _lineBuilder = lineBuilder;
    }

    public OverlayOutputComposition Compose(LibreSensorSnapshot snapshot, IReadOnlyCollection<OverlayGroup> overlayGroups)
    {
        var groups = overlayGroups.OrderBy(group => group.Order).ToArray();

        var totalSelectedSensorCount = groups.Sum(group => group.Sensors.Count);
        var enabledSelectedSensorCount = groups
            .Where(group => group.Enabled)
            .Sum(group => group.Sensors.Count(sensor => sensor.Enabled));

        var usedDefaultSelection = totalSelectedSensorCount == 0;
        var line = usedDefaultSelection
            ? _lineBuilder.BuildDefaultLine(snapshot)
            : _lineBuilder.BuildLine(snapshot, groups);

        return new OverlayOutputComposition(line, enabledSelectedSensorCount, totalSelectedSensorCount, usedDefaultSelection);
    }
}
