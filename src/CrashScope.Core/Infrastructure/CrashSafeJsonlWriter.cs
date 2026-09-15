using System.Text;
using System.Text.Json;

namespace CrashScope.Core.Infrastructure;

public sealed class CrashSafeJsonlWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly StreamWriter _writer;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();

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
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        lock (_gate)
        {
            _writer.WriteLine(json);
            _writer.Flush();
            _stream.Flush(flushToDisk: true);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Flush();
            _stream.Flush(flushToDisk: true);
            _writer.Dispose();
            _stream.Dispose();
        }
    }
}
