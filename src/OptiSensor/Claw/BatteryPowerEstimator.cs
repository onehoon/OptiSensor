namespace OptiSensor.Claw;

/// <summary>
/// Calculates remaining battery time from a short rolling average of validated EC discharge
/// power. Input timestamps must be monotonic; the sampler supplies <see cref="Stopwatch"/>
/// elapsed time, while tests supply fixed values.
/// </summary>
internal sealed class BatteryPowerEstimator
{
    private const int MinimumSamples = 10;
    private static readonly TimeSpan MinimumHistory = TimeSpan.FromSeconds(9);
    private static readonly TimeSpan RollingWindow = TimeSpan.FromSeconds(20);

    private readonly Queue<Sample> _samples = new();

    public void Observe(bool onBattery, double? powerW, TimeSpan now)
    {
        if (!onBattery)
        {
            Reset();
            return;
        }

        if (powerW is not { } power || !double.IsFinite(power) || power <= 0)
            return;

        _samples.Enqueue(new Sample(now, power));
        while (_samples.Count > 0 && now - _samples.Peek().Timestamp > RollingWindow)
            _samples.Dequeue();
    }

    public bool Ready => _samples.Count >= MinimumSamples &&
        _samples.Last().Timestamp - _samples.Peek().Timestamp >= MinimumHistory;

    public int SampleCount => _samples.Count;

    public double? AveragePowerW
    {
        get
        {
            if (!Ready)
                return null;

            var average = _samples.Average(sample => sample.PowerW);
            return double.IsFinite(average) && average > 0 ? average : null;
        }
    }

    public int? EstimateRemainingMinutes(uint? remainingCapacityMWh)
    {
        if (AveragePowerW is not { } averagePowerW || remainingCapacityMWh is not { } capacity)
            return null;

        var minutes = capacity / 1000.0 / averagePowerW * 60.0;
        return double.IsFinite(minutes) && minutes >= 0
            ? (int)Math.Round(minutes, MidpointRounding.AwayFromZero)
            : null;
    }

    public void Reset() => _samples.Clear();

    private sealed record Sample(TimeSpan Timestamp, double PowerW);
}
