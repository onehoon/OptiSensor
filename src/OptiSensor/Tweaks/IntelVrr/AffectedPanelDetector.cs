using System.Management;

namespace OptiSensor.Tweaks.IntelVrr;

/// <summary>Identity of a detected internal panel, read from Windows monitor/EDID data (not the
/// friendly machine name - EDID manufacturer/product code/panel name are the durable identity).</summary>
internal sealed record PanelIdentity(string ManufacturerCode, string ProductCodeHex, string? PanelName);

/// <summary>
/// Detects whether the affected MSI Claw 8 internal panel is present, using Windows'
/// WmiMonitorID (EDID-derived) instance data - independent of the machine's friendly model name,
/// which is not reliable for panel identification across BIOS/OEM SKUs.
/// </summary>
internal static class AffectedPanelDetector
{
    /// <summary>EDID manufacturer ID for the affected panel vendor ("CSW" = Chuangxin/AUO-family
    /// panel house code used on the MSI Claw 8 internal panel).</summary>
    private const string AffectedManufacturerCode = "CSW";

    /// <summary>EDID product code for the affected panel, as a 4-digit hex string.</summary>
    private const string AffectedProductCodeHex = "0801";

    /// <summary>EDID-reported descriptive panel name.</summary>
    private const string AffectedPanelName = "PN8007QB1-2";

    /// <summary>Requires ALL THREE EDID identity fields (manufacturer, product code, and panel
    /// name) to be present and matching. A missing/unreadable panel name is deliberately NOT
    /// treated as a match, even if manufacturer and product code agree - this feature auto-mutates
    /// driver state, so an incomplete identity must fail open (not affected) rather than risk
    /// matching the wrong panel.</summary>
    public static bool IsAffectedPanel(PanelIdentity identity)
    {
        return string.Equals(identity.ManufacturerCode, AffectedManufacturerCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(identity.ProductCodeHex, AffectedProductCodeHex, StringComparison.OrdinalIgnoreCase)
            && identity.PanelName is not null
            && identity.PanelName.Contains(AffectedPanelName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Enumerates all connected monitors' EDID identities via WMI (root\wmi, WmiMonitorID).
    /// Returns an empty list (never throws) if WMI is unavailable.</summary>
    public static IReadOnlyList<PanelIdentity> EnumeratePanelIdentities()
    {
        var results = new List<PanelIdentity>();

        try
        {
            using var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorID");
            using var collection = searcher.Get();

            foreach (ManagementObject monitor in collection)
            {
                using (monitor)
                {
                    var manufacturer = DecodeUShortArrayAsString(monitor["ManufacturerName"] as ushort[]);
                    var productCodeRaw = monitor["ProductCodeID"] as ushort[];
                    var name = DecodeUShortArrayAsString(monitor["UserFriendlyName"] as ushort[]);

                    if (manufacturer is null)
                        continue;

                    // ProductCodeID is the raw 2-byte little-endian EDID product code, each WMI
                    // array element holding one byte (0-255) - not ASCII, unlike the name fields.
                    var productCodeHex = DecodeProductCodeHex(productCodeRaw);

                    results.Add(new PanelIdentity(manufacturer, productCodeHex, name));
                }
            }
        }
        catch (Exception)
        {
            // WMI unavailable / access denied - treat as "no panel detected", never throw.
            return [];
        }

        return results;
    }

    private static string DecodeProductCodeHex(ushort[]? bytes)
    {
        if (bytes is not { Length: >= 2 })
            return "0000";

        var productCode = (ushort)(bytes[0] | (bytes[1] << 8));
        return productCode.ToString("X4");
    }

    private static string? DecodeUShortArrayAsString(ushort[]? values)
    {
        if (values is null || values.Length == 0)
            return null;

        var chars = values.Where(v => v != 0).Select(v => (char)v).ToArray();
        return chars.Length == 0 ? null : new string(chars);
    }
}
