using System.Text;

namespace RdtClient.Service.Services.Usenet.Par2.Packets;

public class FileDesc(Par2PacketHeader header) : Par2Packet(header)
{
    public const String PacketType = "PAR 2.0\0FileDesc";

    public Byte[]? FileID { get; protected set; }
    public Byte[]? FileHash { get; protected set; }
    public Byte[]? File16kHash { get; protected set; }
    public UInt64 FileLength { get; protected set; }
    public String? FileName { get; protected set; }

    protected override void ParseBody(Byte[] body)
    {
        FileID = new Byte[16];
        Buffer.BlockCopy(body, 0, FileID, 0, 16);

        FileHash = new Byte[16];
        Buffer.BlockCopy(body, 16, FileHash, 0, 16);

        File16kHash = new Byte[16];
        Buffer.BlockCopy(body, 32, File16kHash, 0, 16);

        FileLength = BitConverter.ToUInt64(body, 48);

        var nameBuffer = new Byte[body.Length - 56];
        Buffer.BlockCopy(body, 56, nameBuffer, 0, nameBuffer.Length);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = IsUTF8(nameBuffer) ? Encoding.UTF8 : Encoding.GetEncoding(1252);
        FileName = encoding.GetString(nameBuffer).Normalize().TrimEnd('\0');
    }

    private static Boolean IsUTF8(Byte[] input)
    {
        if (input == null || input.Length == 0) return false;

        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] < 0x80) continue;

            if ((input[i] & 0xF8) == 0xF0)
            {
                if (i + 3 >= input.Length) return false;
                if ((input[i + 1] & 0xC0) != 0x80) return false;
                if ((input[i + 2] & 0xC0) != 0x80) return false;
                if ((input[i + 3] & 0xC0) != 0x80) return false;
                i += 3;
                continue;
            }

            if ((input[i] & 0xF0) == 0xE0)
            {
                if (i + 2 >= input.Length) return false;
                if ((input[i + 1] & 0xC0) != 0x80) return false;
                if ((input[i + 2] & 0xC0) != 0x80) return false;
                i += 2;
                continue;
            }

            if ((input[i] & 0xE0) == 0xC0)
            {
                if (i + 1 >= input.Length) return false;
                if ((input[i + 1] & 0xC0) != 0x80) return false;
                i += 1;
                continue;
            }
        }

        return true;
    }

    public override String ToString() => FileName ?? "FileDesc";
}
