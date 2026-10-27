using System.Text;
using LuxEngine.Output.Protocols;

namespace LuxEngine.Output.Tests;

public class PacketTests
{
    [Fact]
    public void ArtDmx_header_matches_specification()
    {
        var data = new byte[512];
        data[0] = 11;
        data[511] = 22;

        var p = ArtNetPacket.BuildDmx(portAddress: 0x1234, data, sequence: 7);

        Assert.Equal(18 + 512, p.Length);
        Assert.Equal("Art-Net\0", Encoding.ASCII.GetString(p, 0, 8));
        Assert.Equal(new byte[] { 0x00, 0x50 }, p[8..10]);   // OpDmx, little-endian
        Assert.Equal(new byte[] { 0x00, 0x0E }, p[10..12]);  // protocol 14
        Assert.Equal(7, p[12]);
        Assert.Equal(0x34, p[14]);                           // SubUni
        Assert.Equal(0x12, p[15]);                           // Net
        Assert.Equal(new byte[] { 0x02, 0x00 }, p[16..18]);  // length 512, big-endian
        Assert.Equal(11, p[18]);
        Assert.Equal(22, p[^1]);
    }

    [Fact]
    public void ArtDmx_length_is_rounded_up_to_even()
    {
        var p = ArtNetPacket.BuildDmx(0, new byte[] { 1, 2, 3 }, 0);
        Assert.Equal(18 + 4, p.Length);
        Assert.Equal(4, p[17]);
    }

    [Fact]
    public void Sacn_packet_layout_matches_E131()
    {
        var data = new byte[512];
        data[0] = 255;
        var cid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        var p = SacnPacket.BuildData(universe: 258, data, sequence: 9, cid, "LuxEngine");

        Assert.Equal(638, p.Length);
        Assert.Equal(new byte[] { 0x00, 0x10, 0x00, 0x00 }, p[0..4]);
        Assert.Equal("ASC-E1.17", Encoding.ASCII.GetString(p, 4, 9));
        Assert.Equal(new byte[] { 0x72, 0x6E }, p[16..18]);                 // root flags+length 622
        Assert.Equal(new byte[] { 0, 0, 0, 4 }, p[18..22]);
        Assert.Equal(new byte[] { 0x00, 0x11, 0x22, 0x33 }, p[22..26]);     // CID in network order
        Assert.Equal(new byte[] { 0x72, 0x58 }, p[38..40]);                 // framing flags+length 600
        Assert.Equal(new byte[] { 0, 0, 0, 2 }, p[40..44]);
        Assert.Equal("LuxEngine", Encoding.UTF8.GetString(p, 44, 9));
        Assert.Equal(0, p[44 + 9]);
        Assert.Equal(100, p[108]);
        Assert.Equal(9, p[111]);
        Assert.Equal(new byte[] { 0x01, 0x02 }, p[113..115]);               // universe 258
        Assert.Equal(new byte[] { 0x72, 0x0B }, p[115..117]);               // DMP flags+length 523
        Assert.Equal(new byte[] { 0x02, 0xA1, 0x00, 0x00, 0x00, 0x01, 0x02, 0x01 }, p[117..125]);
        Assert.Equal(0, p[125]);                                            // start code
        Assert.Equal(255, p[126]);
    }

    [Fact]
    public void Sacn_multicast_address_uses_universe_bytes()
    {
        Assert.Equal("239.255.1.2", SacnPacket.MulticastAddress(258).ToString());
    }

    [Fact]
    public void Enttec_pro_message_wraps_start_code_and_data()
    {
        var data = new byte[512];
        data[0] = 42;
        var m = EnttecProPacket.BuildDmx(data);
        Assert.Equal(518, m.Length);
        Assert.Equal(new byte[] { 0x7E, 6, 0x01, 0x02, 0x00, 42 }, m[0..6]); // 513 = 0x0201
        Assert.Equal(0xE7, m[^1]);
    }
}
