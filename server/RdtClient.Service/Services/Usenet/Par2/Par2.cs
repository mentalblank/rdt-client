using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using RdtClient.Service.Services.Usenet.Par2.Packets;

namespace RdtClient.Service.Services.Usenet.Par2;

public class Par2
{
    internal static readonly Regex ParVolume = new(
        @"(.+)\.vol[0-9]{1,10}\+[0-9]{1,10}\.par2$",
        RegexOptions.IgnoreCase
    );

    private const String Par2PacketHeaderMagic = "PAR2\0PKT";

    public static async IAsyncEnumerable<FileDesc> ReadFileDescriptions(Stream stream, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        while (stream.Position < stream.Length && !ct.IsCancellationRequested)
        {
            Par2Packet? packet;
            try
            {
                packet = await ReadPacketAsync(stream).ConfigureAwait(false);
            }
            catch
            {
                yield break;
            }

            if (packet is FileDesc newFile)
            {
                yield return newFile;
            }
        }
    }

    private static async Task<Par2Packet> ReadPacketAsync(Stream stream)
    {
        var header = await ReadStructAsync<Par2PacketHeader>(stream).ConfigureAwait(false);

        var magic = Encoding.ASCII.GetString(header.Magic);
        if (!Par2PacketHeaderMagic.Equals(magic))
            throw new ApplicationException("Invalid Magic Constant");

        var packetType = Encoding.ASCII.GetString(header.PacketType);
        Par2Packet result = packetType == FileDesc.PacketType ? new FileDesc(header) : new Par2Packet(header);

        await result.ReadAsync(stream).ConfigureAwait(false);

        return result;
    }

    private static async Task<T> ReadStructAsync<T>(Stream stream) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var buffer = new Byte[size];
        await stream.ReadExactlyAsync(buffer.AsMemory(0, size)).ConfigureAwait(false);
        var pinnedBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var structure = Marshal.PtrToStructure<T>(pinnedBuffer.AddrOfPinnedObject());
            return structure;
        }
        finally
        {
            pinnedBuffer.Free();
        }
    }

    private static T ReadStruct<T>(Byte[] bytes) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        if (bytes.Length < size)
        {
            throw new ArgumentException("Byte array is too short to represent the struct.", nameof(bytes));
        }

        var pinnedBuffer = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var structure = Marshal.PtrToStructure<T>(pinnedBuffer.AddrOfPinnedObject());
            return structure;
        }
        finally
        {
            pinnedBuffer.Free();
        }
    }

    public static Boolean HasPar2MagicBytes(Byte[] bytes)
    {
        try
        {
            var header = ReadStruct<Par2PacketHeader>(bytes);
            var magic = Encoding.ASCII.GetString(header.Magic);
            return Par2PacketHeaderMagic.Equals(magic);
        }
        catch
        {
            return false;
        }
    }
}
