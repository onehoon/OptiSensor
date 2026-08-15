using System.Management;

namespace OptiSensor.Tweaks.IntelVrr;

/// <summary>Identity of a detected internal panel, read from Windows monitor/EDID data (not the
/// friendly machine name - EDID manufacturer/product code/panel name are the durable identity).
/// <paramref name="Active"/> IS part of the match decision in
/// <see cref="AffectedPanelDetector.IsAffectedPanel"/> (see remarks there); <paramref name="InstanceName"/>
/// is carried through purely for diagnostics (see <see cref="AffectedPanelDetector.EnumeratePanelIdentities"/>).</summary>
internal sealed record PanelIdentity(
    string ManufacturerCode,
    string ProductCodeId,
    string? PanelName,
    bool Active = false,
    string? InstanceName = null);

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

    /// <summary>EDID product code for the affected panel, decoded as a character string (WMI
    /// exposes it as a ushort[] of character codes, same as ManufacturerName/UserFriendlyName -
    /// NOT as two raw packed EDID bytes).</summary>
    private const string AffectedProductCodeId = "0801";

    /// <summary>EDID-reported descriptive panel name.</summary>
    private const string AffectedPanelName = "PN8007QB1-2";

    /// <summary>Requires ALL THREE EDID identity fields (manufacturer, product code, and panel
    /// name) to be present and matching, AND <see cref="PanelIdentity.Active"/> to be true. A
    /// missing/unreadable panel name is deliberately NOT treated as a match, even if manufacturer
    /// and product code agree - this feature auto-mutates driver state, so an incomplete identity
    /// must fail open (not affected) rather than risk matching the wrong panel.
    ///
    /// The <c>Active</c> requirement closes a related safety gap: PR1 does not yet have full WMI
    /// monitor-instance-path -> IGCL display-output correlation (see the known limitation noted on
    /// <c>IntelVrrRangeTweak.ResolveSingleCandidate</c>). If the internal Claw panel is disabled in
    /// Windows (e.g. only an external display is active) while its WmiMonitorID entry still exists
    /// with <c>Active=false</c>, treating that stale entry as a match could cause the single-candidate
    /// conservative fallback to select and mutate an unrelated external Arc-Sync-capable display
    /// instead of correctly doing nothing.</summary>
    public static bool IsAffectedPanel(PanelIdentity identity)
    {
        return identity.Active
            && string.Equals(identity.ManufacturerCode, AffectedManufacturerCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(identity.ProductCodeId, AffectedProductCodeId, StringComparison.OrdinalIgnoreCase)
            && identity.PanelName is not null
            && identity.PanelName.Contains(AffectedPanelName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Enumerates all connected monitors' EDID identities via WMI (root\wmi, WmiMonitorID).
    /// Deliberately does NOT catch WMI/COM exceptions here - a genuine "WMI query failed" (provider
    /// unavailable, access denied, COM error, etc.) must be distinguishable from a successful "zero
    /// displays enumerated" result. Callers that need "never throw" semantics (e.g.
    /// <c>IntelVrrRangeTweak</c>) are responsible for catching and reporting the failure explicitly
    /// rather than having it silently collapse into an empty list here.</summary>
    public static IReadOnlyList<PanelIdentity> EnumeratePanelIdentities()
    {
        var results = new List<PanelIdentity>();

        using var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorID");
        using var collection = searcher.Get();

        foreach (ManagementObject monitor in collection)
        {
            using (monitor)
            {
                var manufacturer = DecodeUShortArrayAsString(monitor["ManufacturerName"] as ushort[]);
                var productCodeId = DecodeUShortArrayAsString(monitor["ProductCodeID"] as ushort[]);
                var name = DecodeUShortArrayAsString(monitor["UserFriendlyName"] as ushort[]);
                var active = monitor["Active"] is bool a && a;
                var instanceName = monitor["InstanceName"] as string;

                if (manufacturer is null)
                    continue;

                results.Add(new PanelIdentity(manufacturer, productCodeId ?? string.Empty, name, active, instanceName));
            }
        }

        return results;
    }

    /// <summary>Decodes a WMI <c>ushort[]</c> character-array field (as used by
    /// <c>WmiMonitorID</c>'s <c>ManufacturerName</c>, <c>ProductCodeID</c>, and
    /// <c>UserFriendlyName</c> - each array element is a character code, e.g. the ASCII digits of
    /// "0801" for ProductCodeID, NOT raw packed EDID bytes) into a string, dropping trailing/embedded
    /// NUL padding. Internal (rather than private) so it can be exercised directly by tests without
    /// requiring a live WMI provider.</summary>
    internal static string? DecodeUShortArrayAsString(ushort[]? values)
    {
        if (values is null || values.Length == 0)
            return null;

        var chars = values.Where(v => v != 0).Select(v => (char)v).ToArray();
        return chars.Length == 0 ? null : new string(chars);
    }
}
