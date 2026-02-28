using System.Runtime.InteropServices;

namespace RdtClient.Service.Services.Usenet.Par2.Packets;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
public struct Par2PacketHeader
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public Byte[] Magic;

    public UInt64 PacketLength;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public Byte[] PacketHash;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public Byte[] RecoverySetID;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public Byte[] PacketType;
}
