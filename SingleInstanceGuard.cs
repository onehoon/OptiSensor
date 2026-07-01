namespace OptiSensor;

internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\OptiSensor.SingleInstance";

    private readonly Mutex _mutex;
    private readonly bool _hasHandle;

    private SingleInstanceGuard(Mutex mutex, bool hasHandle)
    {
        _mutex = mutex;
        _hasHandle = hasHandle;
    }

    public static SingleInstanceGuard? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
            return new SingleInstanceGuard(mutex, hasHandle: true);

        try
        {
            if (mutex.WaitOne(TimeSpan.Zero))
                return new SingleInstanceGuard(mutex, hasHandle: true);
        }
        catch (AbandonedMutexException)
        {
            return new SingleInstanceGuard(mutex, hasHandle: true);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_hasHandle)
            _mutex.ReleaseMutex();

        _mutex.Dispose();
    }
}
