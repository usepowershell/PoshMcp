using System.Buffers.Binary;
using System.Text.Json;

namespace PoshMcp.Foundation;

public static class Framing
{
    public static async Task WriteAsync(Stream stream, WorkerEnvelope message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Contract.Json);
        if (bytes.Length > Contract.MaxFrameBytes)
            throw new InvalidDataException("Worker frame exceeds the byte budget.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<WorkerEnvelope> ReadAsync(Stream stream, string kind, string id, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > Contract.MaxFrameBytes)
            throw new InvalidDataException("Invalid worker frame size.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        var message = JsonSerializer.Deserialize<WorkerEnvelope>(bytes, Contract.Json)
            ?? throw new InvalidDataException("Null worker envelope.");
        if (message.Version != Contract.WorkerVersion || message.Kind != kind || message.Id != id)
            throw new InvalidDataException("Worker version, state or correlation mismatch.");
        return message;
    }

    public static WorkerEnvelope Message(string kind, string id, object payload) =>
        new(Contract.WorkerVersion, kind, id, JsonSerializer.SerializeToElement(payload, Contract.Json));
}
