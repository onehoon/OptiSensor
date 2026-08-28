using System.Runtime.InteropServices;

namespace OptiSensor.Claw;

/// <summary>
/// Windows battery / AC-line telemetry. Direct C# port of ClawHUD's
/// <c>WindowsPowerTelemetry</c> (<c>GetSystemPowerStatus</c> + sentinel decoding).
/// <c>null</c> fields mean Windows reported the value as unavailable.
/// </summary>
internal sealed record WindowsPowerSnapshot(
    int? BatteryPercent,
    int? RemainingMinutes,
    bool? OnBattery);

internal static class WindowsPowerTelemetry
{
    /// <summary>
    /// Reads a fresh power snapshot. Returns <c>null</c> only when
    /// <c>GetSystemPowerStatus</c> itself fails - individual fields are decoded independently.
    /// </summary>
    public static WindowsPowerSnapshot? Read()
    {
        return GetSystemPowerStatus(out var status) ? Decode(status) : null;
    }

    internal static WindowsPowerSnapshot Decode(SYSTEM_POWER_STATUS status)
    {
        int? batteryPercent = status.BatteryLifePercent == 255
            ? null
            : status.BatteryLifePercent;

        bool? onBattery = status.ACLineStatus == 255
            ? null
            : status.ACLineStatus == 0;

        int? remainingMinutes = status.BatteryLifeTime == uint.MaxValue
            ? null
            : (int)(status.BatteryLifeTime / 60);

        return new WindowsPowerSnapshot(batteryPercent, remainingMinutes, onBattery);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
