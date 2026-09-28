namespace CrashScope.Desktop;

/// <summary>Serializes async UI requests; the recorder keeps its existing lifecycle lock.</summary>
public sealed class RecorderOperationGate
{
    private int _pending;
    public int Pending => Volatile.Read(ref _pending); // 0 none, 1 start, 2 stop
    public string? LastError { get; private set; }
    public event Action? Changed;

    public async Task<bool> RunAsync(bool starting, Func<Task> operation)
    {
        if (Interlocked.CompareExchange(ref _pending, starting ? 1 : 2, 0) != 0) return false;
        try
        {
            LastError = null;
            Changed?.Invoke();
            await operation();
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; throw; }
        finally { Volatile.Write(ref _pending, 0); Changed?.Invoke(); }
    }
}
