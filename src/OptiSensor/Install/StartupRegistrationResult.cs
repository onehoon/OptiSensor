namespace OptiSensor.Install;

internal sealed record StartupRegistrationResult(
    bool Success,
    bool Registered,
    string? ErrorMessage)
{
    public static StartupRegistrationResult Ok(bool registered)
    {
        return new StartupRegistrationResult(true, registered, null);
    }

    public static StartupRegistrationResult Failed(string errorMessage)
    {
        return new StartupRegistrationResult(false, false, errorMessage);
    }
}
