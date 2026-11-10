using GameGuard.Core.Ipc;
using GameGuard.Infrastructure.Ipc;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class IpcMessageFramerTests
{
    [Fact]
    public async Task WriteAndRead_RoundtripsSuccessfully()
    {
        using var stream = new MemoryStream();

        var message = IpcMessage.CreateRequest("GetStatus", "{\"test\":123}");
        await IpcMessageFramer.WriteMessageAsync(stream, message);

        stream.Position = 0;

        var readMessage = await IpcMessageFramer.ReadMessageAsync(stream);

        Assert.NotNull(readMessage);
        Assert.Equal(message.Id, readMessage.Id);
        Assert.Equal("Request", readMessage.Type);
        Assert.Equal("GetStatus", readMessage.Action);
        Assert.Equal("{\"test\":123}", readMessage.PayloadJson);
    }

    [Fact]
    public async Task Read_WhenStreamEmpty_ReturnsNull()
    {
        using var stream = new MemoryStream();
        var readMessage = await IpcMessageFramer.ReadMessageAsync(stream);
        Assert.Null(readMessage);
    }

    [Fact]
    public async Task Read_WhenOversizedLength_ThrowsException()
    {
        using var stream = new MemoryStream();
        // Write a 4-byte length prefix exceeding MaxPayloadSize (e.g. 5 MB)
        byte[] lengthBytes = BitConverter.GetBytes(5 * 1024 * 1024);
        await stream.WriteAsync(lengthBytes);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => IpcMessageFramer.ReadMessageAsync(stream));
    }
}
