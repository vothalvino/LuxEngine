using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace LuxEngine.Output.Protocols;

/// <summary>Builds ANSI E1.31 (sACN) data packets.</summary>
public static class SacnPacket
{
    public const int Port = 5568;
    public const int PacketLength = 638;
    public const byte DefaultPriority = 100;

    private static readonly byte[] AcnPacketIdentifier =
        { 0x41, 0x53, 0x43, 0x2D, 0x45, 0x31, 0x2E, 0x31, 0x37, 0x00, 0x00, 0x00 };

    /// <summary>Multicast group for a universe: 239.255.{hi}.{lo}.</summary>
    public static IPAddress MulticastAddress(int universe) =>
        new(new byte[] { 239, 255, (byte)(universe >> 8), (byte)(universe & 0xFF) });

    /// <param name="universe">sACN universe, 1–63999.</param>
    public static byte[] BuildData(int universe, ReadOnlySpan<byte> data, byte sequence, Guid cid,
        string sourceName, byte priority = DefaultPriority)
    {
        if (universe is < 1 or > 63999) throw new ArgumentOutOfRangeException(nameof(universe));
        var p = new byte[PacketLength];
        var span = p.AsSpan();

        // Root layer
        BinaryPrimitives.WriteUInt16BigEndian(span[0..], 0x0010);               // Preamble size
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], 0x0000);               // Post-amble size
        AcnPacketIdentifier.CopyTo(span[4..]);
        BinaryPrimitives.WriteUInt16BigEndian(span[16..], FlagsAndLength(PacketLength - 16));
        BinaryPrimitives.WriteUInt32BigEndian(span[18..], 0x00000004);          // VECTOR_ROOT_E131_DATA
        WriteCid(span[22..38], cid);

        // Framing layer
        BinaryPrimitives.WriteUInt16BigEndian(span[38..], FlagsAndLength(PacketLength - 38));
        BinaryPrimitives.WriteUInt32BigEndian(span[40..], 0x00000002);          // VECTOR_E131_DATA_PACKET
        var name = Encoding.UTF8.GetBytes(sourceName);
        name.AsSpan(0, Math.Min(name.Length, 63)).CopyTo(span[44..108]);         // null-terminated, 64 bytes
        p[108] = Math.Min(priority, (byte)200);
        BinaryPrimitives.WriteUInt16BigEndian(span[109..], 0);                  // Synchronization address
        p[111] = sequence;
        p[112] = 0;                                                             // Options
        BinaryPrimitives.WriteUInt16BigEndian(span[113..], (ushort)universe);

        // DMP layer
        BinaryPrimitives.WriteUInt16BigEndian(span[115..], FlagsAndLength(PacketLength - 115));
        p[117] = 0x02;                                                          // VECTOR_DMP_SET_PROPERTY
        p[118] = 0xA1;                                                          // Address & data type
        BinaryPrimitives.WriteUInt16BigEndian(span[119..], 0x0000);             // First property address
        BinaryPrimitives.WriteUInt16BigEndian(span[121..], 0x0001);             // Address increment
        BinaryPrimitives.WriteUInt16BigEndian(span[123..], 513);                // Property value count
        p[125] = 0x00;                                                          // DMX start code
        data[..Math.Min(data.Length, 512)].CopyTo(span[126..]);
        return p;
    }

    private static ushort FlagsAndLength(int length) => (ushort)(0x7000 | (length & 0x0FFF));

    /// <summary>Writes the CID in RFC 4122 network byte order (as printed by Guid.ToString()).</summary>
    private static void WriteCid(Span<byte> destination, Guid cid)
    {
        cid.TryWriteBytes(destination, bigEndian: true, out _);
    }
}
