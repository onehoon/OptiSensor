namespace OptiSensor.Native;

internal sealed class SensorValueRetention
{
    private const int FailureLimit = 3;
    private int _consecutiveMisses;

    public float? LastValidValue { get; private set; }

    public float? RecordPollResult(bool pollAttempted, bool updateSucceeded, float? value)
    {
        if (!pollAttempted)
        {
            return LastValidValue;
        }

        if (updateSucceeded && value is float validValue && float.IsFinite(validValue))
        {
            LastValidValue = validValue;
            _consecutiveMisses = 0;
            return LastValidValue;
        }

        if (++_consecutiveMisses >= FailureLimit)
        {
            LastValidValue = null;
        }

        return LastValidValue;
    }
}
