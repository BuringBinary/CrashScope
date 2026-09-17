namespace CrashScope.Core.Infrastructure;

/// <summary>Process-independent ownership. Never delete the lock file: an open handle is the lease.</summary>
public sealed class DataRootLease : IDisposable
{
    private readonly FileStream _handle;
    private DataRootLease(FileStream handle) => _handle = handle;

    public static DataRootLease? TryAcquire(string stateDirectory)
    {
        Directory.CreateDirectory(stateDirectory);
        try
        {
            return new DataRootLease(new FileStream(Path.Combine(stateDirectory, "recorder.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException) { return null; }
    }

    public static DataRootLease Acquire(string stateDirectory) => TryAcquire(stateDirectory)
        ?? throw new InvalidOperationException("This data root is already owned by a recorder/recovery operation, or its lock is unavailable. Connect to the existing Agent instead.");

    public void Dispose() => _handle.Dispose();
}
