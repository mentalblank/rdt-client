namespace RdtClient.Service.Services.Usenet.Streams;

public abstract class FastReadOnlyNonSeekableStream : FastReadOnlyStream
{
    public override Boolean CanSeek => false;
    public override Int64 Length => throw new NotSupportedException();

    public override Int64 Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void Flush() { }
}
