using OptiSensor.Models;

namespace OptiSensor.Settings;

internal sealed record GeneralSettingsDraft(
    bool StartWithWindows,
    SensorSourceKind SensorSource,
    int PublishIntervalMs);
