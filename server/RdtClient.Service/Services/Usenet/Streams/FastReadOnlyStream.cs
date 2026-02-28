namespace RdtClient.Service.Services.Usenet.Streams;

public abstract class FastReadOnlyStream : Stream
{
    public override Boolean CanRead => true;
    public override Boolean CanWrite => false;

    public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count)
    {
        return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    }

    public override void SetLength(Int64 value) => throw new NotSupportedException();
    public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();
    public override void WriteByte(Byte value) => throw new NotSupportedException();
    public override Task WriteAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken) => throw new NotSupportedException();
    public override ValueTask WriteAsync(ReadOnlyMemory<Byte> buffer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
