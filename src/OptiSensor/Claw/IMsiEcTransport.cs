namespace OptiSensor.Claw;

/// <summary>
/// Read-only MSI ACPI EC transport seam. One implementation
/// (<see cref="MsiEcWmiTransport"/>); the interface exists only so the telemetry
/// reader can be unit tested without live WMI.
/// </summary>
internal interface IMsiEcTransport
{
    /// <summary>
    /// Invokes an MSI EC read method (e.g. <c>Get_Temperature</c>, <c>Get_Fan</c>,
    /// <c>Get_Data</c>) with the given selector. On success returns the logical payload
    /// (the MSI response bytes after the leading success flag). Returns false with an
    /// empty payload on any transport/response failure.
    /// </summary>
    bool TryRead(string method, int selector, out byte[] payload);
}
