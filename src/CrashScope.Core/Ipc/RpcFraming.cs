using System.Buffers.Binary;
using System.Text.Json;
using CrashScope.Contracts;

namespace CrashScope.Core.Ipc;

/// <summary>
/// Frame codec for the RPC transport: a 4-byte little-endian length followed by that
/// many bytes of UTF-8 JSON. Kept dumb on purpose - a Wireshark-style debug session
/// should be able to read this protocol.
/// </summary>
public static class RpcFraming
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Reads one envelope. Returns null on a clean EOF at a frame boundary; throws
    /// EndOfStreamException on a truncated frame and InvalidDataException on an
    /// out-of-range length.
    /// </summary>
    public static async Task<RpcEnvelope?> ReadEnvelopeAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        var headerRead = await TryReadExactAsync(stream, header, ct).ConfigureAwait(false);
        if (headerRead == 0)
            return null;
        if (headerRead < header.Length)
            throw new EndOfStreamException("RPC stream ended inside the 4-byte frame header.");

        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length == 0 || length > RpcProtocol.MaxFrameBytes)
            throw new InvalidDataException($"RPC frame length {length} is outside the allowed range.");

        var body = new byte[length];
        var bodyRead = await TryReadExactAsync(stream, body, ct).ConfigureAwait(false);
        if (bodyRead < body.Length)
            throw new EndOfStreamException("RPC stream ended in the middle of a frame body.");

        return JsonSerializer.Deserialize<RpcEnvelope>(body, Options);
    }

    public static async Task WriteEnvelopeAsync(Stream stream, RpcEnvelope envelope, CancellationToken ct)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(envelope, Options);
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)body.Length);
        await stream.WriteAsync(header.AsMemory(), ct).ConfigureAwait(false);
        await stream.WriteAsync(body.AsMemory(), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static T? DeserializePayload<T>(RpcEnvelope envelope) where T : class
        => envelope.Payload is { } payload ? payload.Deserialize<T>(Options) : null;

    /// <summary>Reads until the buffer is full. Returns the number of bytes read; 0 means clean EOF.</summary>
    private static async Task<int> TryReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct).ConfigureAwait(false);
            if (read == 0)
                break;
            total += read;
        }

        return total;
    }
}
