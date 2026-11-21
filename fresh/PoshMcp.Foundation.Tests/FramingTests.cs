using System.Buffers.Binary;

namespace PoshMcp.Foundation.Tests;

[Trait("Category", "Unit")]
public sealed class FramingTests
{
    [Fact]
    public async Task UnicodeFrameRoundTripsOnPrivateChannel()
    {
        using var stream = new MemoryStream();
        await Framing.WriteAsync(stream, Framing.Message("result", "id", new { text = "\u03bb\n\"multiline\"" }), default);
        stream.Position = 0;
        var message = await Framing.ReadAsync(stream, "result", "id", default);
        Assert.Equal("\u03bb\n\"multiline\"", message.Payload.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1048577)]
    public async Task InvalidFrameLengthFailsBeforeAllocation(int length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.ReadAsync(stream, "result", "id", default));
    }

    [Fact]
    public async Task WrongCorrelationAndTruncatedFramesFail()
    {
        using var stream = new MemoryStream();
        await Framing.WriteAsync(stream, Framing.Message("ready", "other", new { }), default);
        stream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.ReadAsync(stream, "ready", "id", default));
        stream.SetLength(6);
        stream.Position = 0;
        await Assert.ThrowsAsync<EndOfStreamException>(() => Framing.ReadAsync(stream, "ready", "other", default));
    }
}
