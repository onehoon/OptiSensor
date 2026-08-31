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
    bool? OnBattery,
    uint? RemainingCapacityMWh = null);

internal static class WindowsPowerTelemetry
{
    /// <summary>
    /// Reads a fresh power snapshot. Returns <c>null</c> only when
    /// <c>GetSystemPowerStatus</c> itself fails - individual fields are decoded independently.
    /// </summary>
    public static WindowsPowerSnapshot? Read()
    {
        if (!GetSystemPowerStatus(out var status))
            return null;

        var snapshot = Decode(status);
        var queryStatus = CallNtPowerInformation(
            PowerInformationLevelSystemBatteryState,
            IntPtr.Zero,
            0,
            out var batteryState,
            (uint)Marshal.SizeOf<SYSTEM_BATTERY_STATE>());
        return WithBatteryState(snapshot, queryStatus, batteryState);
    }

    internal static WindowsPowerSnapshot Decode(SYSTEM_POWER_STATUS status)
    {
        int? batteryPercent = status.BatteryLifePercent == 255
            ? null
            : status.BatteryLifePercent;

        bool? onBattery = status.ACLineStatus == 255
            ? null
            : status.ACLineStatus == 0;

        // BatteryLifeTime is unknown on the supported Claw hardware and is no longer a
        // remaining-time source. Remaining time is derived from EC discharge power instead.
        return new WindowsPowerSnapshot(batteryPercent, null, onBattery, null);
    }

    internal static WindowsPowerSnapshot WithBatteryState(
        WindowsPowerSnapshot snapshot,
        int queryStatus,
        SYSTEM_BATTERY_STATE batteryState)
    {
        return queryStatus >= 0 && batteryState.BatteryPresent != 0 &&
            batteryState.RemainingCapacity != uint.MaxValue
            ? snapshot with { RemainingCapacityMWh = batteryState.RemainingCapacity }
            : snapshot;
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_BATTERY_STATE
    {
        public byte AcOnLine;
        public byte BatteryPresent;
        public byte Charging;
        public byte Discharging;
        public byte Spare1_0;
        public byte Spare1_1;
        public byte Spare1_2;
        public byte Spare1_3;
        public uint MaxCapacity;
        public uint RemainingCapacity;
        public uint Rate;
        public uint EstimatedTime;
        public uint DefaultAlert1;
        public uint DefaultAlert2;
    }

    private const int PowerInformationLevelSystemBatteryState = 5;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [DllImport("powrprof.dll")]
    private static extern int CallNtPowerInformation(
        int informationLevel,
        IntPtr inputBuffer,
        uint inputBufferLength,
        out SYSTEM_BATTERY_STATE outputBuffer,
        uint outputBufferLength);
}
