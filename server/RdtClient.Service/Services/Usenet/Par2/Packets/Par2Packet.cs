using System.Runtime.InteropServices;

namespace RdtClient.Service.Services.Usenet.Par2.Packets;

public class Par2Packet(Par2PacketHeader header)
{
    public Par2PacketHeader Header { get; protected set; } = header;

    public async Task ReadAsync(Stream stream)
    {
        var bodyLength = Header.PacketLength - (UInt64)Marshal.SizeOf<Par2PacketHeader>();

        var body = new Byte[bodyLength];
        await stream.ReadExactlyAsync(body.AsMemory(0, (Int32)bodyLength)).ConfigureAwait(false);

        ParseBody(body);
    }

    protected virtual void ParseBody(Byte[] body)
    {
    }
}
