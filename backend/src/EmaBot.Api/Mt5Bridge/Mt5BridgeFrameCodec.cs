using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace EmaBot.Api.Mt5Bridge;

public sealed class Mt5BridgeFrameCodec(int maxFrameBytes)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly int _maxFrameBytes = maxFrameBytes > 0 ? maxFrameBytes : throw new ArgumentOutOfRangeException(nameof(maxFrameBytes));

    public async Task WriteAsync(Stream stream, Mt5BridgeEnvelope envelope, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, Mt5BridgeProtocol.JsonOptions);
        if (payload.Length == 0 || payload.Length > _maxFrameBytes) throw new Mt5BridgeProtocolException("Bridge frame length is invalid.");
        var header = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(header, checked((uint)payload.Length));
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public async Task<Mt5BridgeEnvelope?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(uint)];
        if (!await ReadExactlyAsync(stream, header, allowCleanEnd: true, cancellationToken)) return null;
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length == 0 || length > _maxFrameBytes) throw new Mt5BridgeProtocolException("Bridge frame length is invalid.");
        var payload = new byte[checked((int)length)];
        await ReadExactlyAsync(stream, payload, allowCleanEnd: false, cancellationToken);
        try
        {
            var json = StrictUtf8.GetString(payload);
            return JsonSerializer.Deserialize<Mt5BridgeEnvelope>(json, Mt5BridgeProtocol.JsonOptions) ?? throw new Mt5BridgeProtocolException("Bridge frame JSON is invalid.");
        }
        catch (DecoderFallbackException) { throw new Mt5BridgeProtocolException("Bridge frame UTF-8 is invalid."); }
        catch (JsonException) { throw new Mt5BridgeProtocolException("Bridge frame JSON is invalid."); }
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, Memory<byte> buffer, bool allowCleanEnd, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
            {
                if (offset == 0 && allowCleanEnd) return false;
                throw new EndOfStreamException("Bridge pipe closed in the middle of a frame.");
            }
            offset += read;
        }
        return true;
    }
}
