namespace WarDogs;

// One UI process per Windows session, across installation directories.
public sealed class SingleInstance : IDisposable
{
    readonly Mutex mutex;
    readonly EventWaitHandle activate;
    readonly RegisteredWaitHandle? listener;
    public bool IsOwner { get; }
    public SingleInstance(Action onActivate, string name = @"Local\WarDogsOverlay.Instance")
    {
        mutex = new Mutex(false, name);
        try { IsOwner = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsOwner = true; }
        activate = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        if (IsOwner) listener = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => onActivate(), null, Timeout.Infinite, false);
        else activate.Set();
    }
    public void Dispose()
    {
        listener?.Unregister(null);
        activate.Dispose();
        if (IsOwner) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
