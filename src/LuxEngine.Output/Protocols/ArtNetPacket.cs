using System.Text;

namespace LuxEngine.Output.Protocols;

/// <summary>Builds Art-Net 4 ArtDmx packets.</summary>
public static class ArtNetPacket
{
    public const int Port = 6454;
    public const ushort OpDmx = 0x5000;
    public const ushort ProtocolVersion = 14;
    public const int HeaderLength = 18;

    private static readonly byte[] Id = Encoding.ASCII.GetBytes("Art-Net\0");

    /// <param name="portAddress">15-bit Art-Net port-address (net, sub-net and universe combined), 0–32767.</param>
    /// <param name="sequence">1–255 to let receivers reorder packets, or 0 to disable.</param>
    public static byte[] BuildDmx(int portAddress, ReadOnlySpan<byte> data, byte sequence)
    {
        if (portAddress is < 0 or > 32767) throw new ArgumentOutOfRangeException(nameof(portAddress));
        int length = Math.Clamp(data.Length, 2, 512);
        if (length % 2 == 1) length++;

        var packet = new byte[HeaderLength + length];
        Id.CopyTo(packet, 0);
        packet[8] = OpDmx & 0xFF;                 // OpCode, little-endian
        packet[9] = OpDmx >> 8;
        packet[10] = ProtocolVersion >> 8;        // Protocol version, big-endian
        packet[11] = ProtocolVersion & 0xFF;
        packet[12] = sequence;
        packet[13] = 0;                           // Physical input port (informational)
        packet[14] = (byte)(portAddress & 0xFF);  // SubUni: sub-net + universe
        packet[15] = (byte)(portAddress >> 8);    // Net
        packet[16] = (byte)(length >> 8);         // Data length, big-endian
        packet[17] = (byte)(length & 0xFF);
        data[..Math.Min(data.Length, length)].CopyTo(packet.AsSpan(HeaderLength));
        return packet;
    }
}
