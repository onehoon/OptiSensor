using System.Management;
using System.Runtime.InteropServices;

namespace OptiSensor.Claw;

/// <summary>
/// MSI ACPI EC read transport over WMI. Ported from SteamAddonforClaw's
/// <c>MsiClawWmiTdpTransport</c> - the proven <see cref="System.Management"/> access
/// pattern, read path only. No <c>Set_Data</c> / TDP / fan / profile control, no helper
/// process, no IPC. Requires an elevated process (OptiSensor Claw runs elevated since PR #48).
/// </summary>
internal sealed class MsiEcWmiTransport : IMsiEcTransport
{
    private const string Scope = @"\\.\root\WMI";
    private const string Path = "MSI_ACPI.InstanceName='ACPI\\PNP0C14\\0_0'";
    private const int PackageLength = 32;

    /// <summary>
    /// Builds the 32-byte MSI request package: <c>package[0]</c> is the selector / data
    /// block, every remaining byte is zero (read calls carry no request payload).
    /// </summary>
    internal static byte[] BuildPackage(int selector)
    {
        if ((uint)selector > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(selector));

        var package = new byte[PackageLength];
        package[0] = (byte)selector;
        return package;
    }

    public MsiEcReadStatus Read(string method, int selector, out byte[] payload)
    {
        payload = [];

        ManagementObject managementObject;
        try { managementObject = new ManagementObject(Scope, Path, null); }
        catch (Exception ex) when (IsExpectedWmiException(ex))
        {
            // The EC accessor itself cannot be created - not a per-metric problem.
            return MsiEcReadStatus.TransportUnavailable;
        }

        using (managementObject)
        {
            ManagementBaseObject? input = null;
            ManagementBaseObject? data = null;
            try
            {
                input = managementObject.GetMethodParameters(method);
                data = input?["Data"] as ManagementBaseObject;
            }
            catch (Exception ex) when (IsExpectedWmiException(ex)) { }

            // Addon compatibility fallback: some MSI ACPI builds do not expose method
            // parameters, so reuse the Data template returned by Get_WMI instead.
            if (input is null || data is null)
            {
                input?.Dispose();
                input = null;
                data = null;
                try
                {
                    input = managementObject.InvokeMethod("Get_WMI", null, null);
                    data = input?["Data"] as ManagementBaseObject;
                }
                catch (Exception ex) when (IsExpectedWmiException(ex))
                {
                    // Primary template lookup already failed and the shared Get_WMI fallback
                    // itself throws a WMI/COM/access error: the EC path is down, not this metric.
                    return MsiEcReadStatus.TransportUnavailable;
                }
            }

            using (input)
            using (data)
            {
                if (input is null || data is null)
                    return MsiEcReadStatus.MetricUnavailable;

                try
                {
                    data["Bytes"] = BuildPackage(selector);
                    input["Data"] = data;
                }
                catch (Exception ex) when (IsExpectedWmiException(ex)) { return Classify(ex); }

                ManagementBaseObject? output;
                try { output = managementObject.InvokeMethod(method, input, null); }
                catch (Exception ex) when (IsExpectedWmiException(ex)) { return Classify(ex); }

                using (output)
                {
                    try
                    {
                        if (output?["Data"] is not ManagementBaseObject response ||
                            response["Bytes"] is not byte[] bytes ||
                            bytes.Length < 1 ||
                            bytes[0] != 1)
                        {
                            return MsiEcReadStatus.MetricUnavailable;
                        }

                        payload = bytes[1..];
                        return MsiEcReadStatus.Success;
                    }
                    catch (Exception ex) when (IsExpectedWmiException(ex)) { return Classify(ex); }
                }
            }
        }
    }

    private static bool IsExpectedWmiException(Exception ex) =>
        ex is ManagementException or COMException or UnauthorizedAccessException;

    /// <summary>
    /// Conservative classification of an already-caught expected WMI failure that occurred after a
    /// usable request template was obtained. Only a clearly global access denial is treated as a
    /// shared-transport failure; anything else stays metric-local so later metrics are still tried.
    /// </summary>
    internal static MsiEcReadStatus Classify(Exception ex) =>
        ex is UnauthorizedAccessException
            ? MsiEcReadStatus.TransportUnavailable
            : MsiEcReadStatus.MetricUnavailable;
}
