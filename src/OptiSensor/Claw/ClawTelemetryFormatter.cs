using System.Globalization;
using System.Text;

namespace OptiSensor.Claw;

/// <summary>
/// Formats a <see cref="ClawTelemetrySnapshot"/> into the plain-text native-telemetry suffix
/// OptiSensor appends to OptiScaler's external overlay. Ports ClawHUD's production
/// <c>FormatHud</c> / <c>JoinHudRuns</c> text contract (segment order, labels, units, rounding,
/// omission): <c>CPU → GPU → TDP → RAM → VRAM → FAN → BAT</c>, joined with <c>" | "</c>. No FPS /
/// graphics-API segment (OptiScaler owns that), no rich text, no colors. Empty string when every
/// field is unavailable.
/// </summary>
internal static class ClawTelemetryFormatter
{
    public static string Format(ClawTelemetrySnapshot snapshot)
    {
        var segments = new List<string>();

        var cpu = CpuValue(snapshot);
        if (cpu.Length > 0)
            segments.Add($"CPU {cpu}");

        var gpu = GpuValue(snapshot);
        if (gpu.Length > 0)
            segments.Add($"GPU {gpu}");

        if (snapshot.CpuPackagePowerW is { } packagePowerW)
            segments.Add($"TDP {packagePowerW}W");

        if (snapshot.SystemMemoryUsedBytes is { } ramBytes)
            segments.Add($"RAM {Gigabytes(ramBytes)}GB");

        if (snapshot.GpuMemoryUsedBytes is { } vramBytes)
            segments.Add($"VRAM {Gigabytes(vramBytes)}GB");

        if (snapshot.FanRpm is { } fanRpm)
            segments.Add($"FAN {fanRpm}RPM");

        if (snapshot.BatteryPercent is { } batteryPercent)
            segments.Add($"BAT {BatteryValue(batteryPercent, snapshot)}");

        return string.Join(" | ", segments);
    }

    private static string CpuValue(ClawTelemetrySnapshot snapshot)
    {
        var value = new StringBuilder();
        if (snapshot.CpuUsagePercent is { } usage)
            value.Append(Integer(usage)).Append('%');
        if (snapshot.CpuTemperatureC is { } temperature)
        {
            if (value.Length > 0)
                value.Append(' ');
            value.Append(temperature).Append("°C");
        }
        return value.ToString();
    }

    private static string GpuValue(ClawTelemetrySnapshot snapshot)
    {
        var value = new StringBuilder();
        if (snapshot.GpuUsagePercent is { } usage)
            value.Append(Integer(usage)).Append('%');
        if (snapshot.GpuClockMHz is { } clock)
        {
            if (value.Length > 0)
                value.Append(' ');
            value.Append(Integer(clock)).Append("MHz");
        }
        return value.ToString();
    }

    private static string BatteryValue(int percent, ClawTelemetrySnapshot snapshot)
    {
        var value = $"{percent}%";

        // ClawHUD appends remaining time only while actually on battery.
        if (snapshot.OnBattery == true && snapshot.RemainingMinutes is { } minutes)
        {
            if (minutes >= 60)
                value += $" {Number(minutes / 60.0)}h";
            else if (minutes >= 0)
                value += $" {minutes}m";
        }

        return value;
    }

    /// <summary>ClawHUD <c>Integer()</c>: <c>std::lround</c> - nearest, ties away from zero.</summary>
    private static string Integer(double value) =>
        ((long)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    /// <summary>ClawHUD <c>Number()</c>: whole number -> no decimal, otherwise one decimal.</summary>
    private static string Number(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return Math.Abs(value - rounded) < 0.001
            ? ((long)rounded).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>ClawHUD <c>Gigabytes()</c>: binary GiB, exactly one decimal place.</summary>
    private static string Gigabytes(ulong bytes) =>
        (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
}
