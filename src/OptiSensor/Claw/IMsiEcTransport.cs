namespace OptiSensor.Claw;

/// <summary>Outcome of a single MSI EC read, so the reader can tell a metric-local miss from a
/// shared-transport failure that the remaining reads in the same sample would just repeat.</summary>
internal enum MsiEcReadStatus
{
    /// <summary>The payload was read.</summary>
    Success,

    /// <summary>This metric is unavailable, but the shared EC access path may still work - the
    /// reader should attempt the remaining metrics.</summary>
    MetricUnavailable,

    /// <summary>Strong evidence that the shared MSI WMI access path itself is down; the remaining
    /// EC reads in this sample would pay the same failure cost and should be skipped.</summary>
    TransportUnavailable,
}

/// <summary>
/// Read-only MSI ACPI EC transport seam. One implementation
/// (<see cref="MsiEcWmiTransport"/>); the interface exists only so the telemetry
/// reader can be unit tested without live WMI.
/// </summary>
internal interface IMsiEcTransport
{
    /// <summary>
    /// Invokes an MSI EC read method (e.g. <c>Get_Temperature</c>, <c>Get_Fan</c>,
    /// <c>Get_Data</c>) with the given selector. On <see cref="MsiEcReadStatus.Success"/> returns
    /// the logical payload (the MSI response bytes after the leading success flag); otherwise the
    /// payload is empty and the status says whether later metrics are still worth attempting.
    /// </summary>
    MsiEcReadStatus Read(string method, int selector, out byte[] payload);
}
