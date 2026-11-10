using System.Buffers.Binary;
using System.Text.Json;
using GameGuard.Core.Ipc;

namespace GameGuard.Infrastructure.Ipc;

public static class IpcMessageFramer
{
    public const int MaxPayloadSize = 1024 * 1024; // 1 MB safety limit

    public static async Task WriteMessageAsync(Stream stream, IpcMessage message, CancellationToken cancellationToken = default)
    {
        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(message);
        if (payloadBytes.Length > MaxPayloadSize)
        {
            throw new InvalidOperationException($"IPC payload size ({payloadBytes.Length} bytes) exceeds maximum limit ({MaxPayloadSize} bytes).");
        }

        byte[] lengthPrefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, payloadBytes.Length);

        await stream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<IpcMessage?> ReadMessageAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] lengthPrefix = new byte[4];
        int bytesRead = await ReadExactBytesAsync(stream, lengthPrefix, 0, 4, cancellationToken).ConfigureAwait(false);
        if (bytesRead == 0)
        {
            // Clean EOF
            return null;
        }

        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
        if (payloadLength <= 0 || payloadLength > MaxPayloadSize)
        {
            throw new InvalidOperationException($"Invalid IPC payload length received: {payloadLength} bytes.");
        }

        byte[] payloadBytes = new byte[payloadLength];
        await ReadExactBytesAsync(stream, payloadBytes, 0, payloadLength, cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Deserialize<IpcMessage>(payloadBytes);
    }

    private static async Task<int> ReadExactBytesAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (totalRead == 0) return 0; // Clean EOF before read started
                throw new EndOfStreamException($"Premature end of stream while reading IPC frame. Expected {count} bytes, got {totalRead}.");
            }
            totalRead += read;
        }
        return totalRead;
    }
}
