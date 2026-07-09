namespace GlassTodo.Services;

/// <summary>
/// Named-mutex single instance. A second launch signals the first via a named event
/// (which summons the panel) and exits.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\GlassTodo.Instance";
    private const string SignalName = @"Local\GlassTodo.Summon";

    private Mutex? _mutex;
    private EventWaitHandle? _signal;
    private RegisteredWaitHandle? _wait;
    private bool _owned;

    /// <summary>Raised on a thread-pool thread when another instance asks us to show the panel.</summary>
    public event Action? SummonRequested;

    /// <summary>True when this process is the primary instance.</summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
        _owned = created;
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);

        if (!created)
        {
            _signal.Set(); // wake the primary instance
            return false;
        }

        _wait = ThreadPool.RegisterWaitForSingleObject(
            _signal, (_, _) => SummonRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);
        return true;
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        if (_owned)
        {
            try { _mutex?.ReleaseMutex(); }
            catch { /* releasing from a different thread than acquisition — ignore */ }
        }
        _mutex?.Dispose();
        _signal?.Dispose();
    }
}
