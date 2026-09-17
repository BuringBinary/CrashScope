using System.Text;
using System.Text.Json;

namespace CrashScope.Core.Infrastructure;

public sealed class CrashSafeJsonlWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly StreamWriter _writer;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private bool _disposed;

    public CrashSafeJsonlWriter(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.WriteThrough | FileOptions.SequentialScan);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true)
        {
            AutoFlush = false
        };
    }

    public void Write<T>(T value)
    {
        lock (_gate)
        {
            // A recorder tick that is still in flight when Stop() disposes the writers
            // must not fault the recording task: the data for this sample is simply
            // dropped, which is already the outcome of a crash mid-tick.
            if (_disposed)
                return;

            var json = JsonSerializer.Serialize(value, _jsonOptions);
            _writer.WriteLine(json);
            _writer.Flush();
            _stream.Flush(flushToDisk: true);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _writer.Flush();
            _stream.Flush(flushToDisk: true);
            _writer.Dispose();
            _stream.Dispose();
        }
    }
}
