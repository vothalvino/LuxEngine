using System.Net;
using System.Net.Sockets;
using LuxEngine.Output.Protocols;

namespace LuxEngine.Output.Drivers;

/// <summary>Sends a universe as Art-Net to a broadcast or unicast address.</summary>
public sealed class ArtNetOutput : DmxOutputBase
{
    private readonly IPEndPoint _target;
    private readonly IPAddress? _local;
    private readonly int _portAddress;
    private UdpClient? _udp;
    private byte _sequence;

    public ArtNetOutput(int universe, string target, int portAddress, string? localAddress) : base(universe)
    {
        _target = new IPEndPoint(IPAddress.Parse(string.IsNullOrWhiteSpace(target) ? "255.255.255.255" : target), ArtNetPacket.Port);
        _local = string.IsNullOrWhiteSpace(localAddress) ? null : IPAddress.Parse(localAddress);
        _portAddress = portAddress;
    }

    public override string Description => $"Art-Net → {_target.Address} (universe {_portAddress})";

    protected override void OpenCore()
    {
        _udp = new UdpClient(new IPEndPoint(_local ?? IPAddress.Any, 0)) { EnableBroadcast = true };
    }

    protected override void SendCore(ReadOnlySpan<byte> data)
    {
        _sequence = (byte)(_sequence == 255 ? 1 : _sequence + 1);
        var packet = ArtNetPacket.BuildDmx(_portAddress, data, _sequence);
        _udp!.Send(packet, packet.Length, _target);
    }

    protected override void CloseCore()
    {
        _udp?.Dispose();
        _udp = null;
    }
}

/// <summary>Sends a universe as sACN (E1.31), multicast by default.</summary>
public sealed class SacnOutput : DmxOutputBase
{
    /// <summary>One CID per app run identifies LuxEngine as a single source to receivers.</summary>
    private static readonly Guid SourceCid = Guid.NewGuid();

    private readonly IPEndPoint _target;
    private readonly IPAddress? _local;
    private readonly int _sacnUniverse;
    private UdpClient? _udp;
    private byte _sequence;

    public SacnOutput(int universe, string target, int sacnUniverse, string? localAddress) : base(universe)
    {
        _sacnUniverse = Math.Max(1, sacnUniverse);
        var address = string.IsNullOrWhiteSpace(target) ? SacnPacket.MulticastAddress(_sacnUniverse) : IPAddress.Parse(target);
        _target = new IPEndPoint(address, SacnPacket.Port);
        _local = string.IsNullOrWhiteSpace(localAddress) ? null : IPAddress.Parse(localAddress);
    }

    public override string Description => $"sACN universe {_sacnUniverse} → {_target.Address}";

    protected override void OpenCore()
    {
        _udp = new UdpClient(new IPEndPoint(_local ?? IPAddress.Any, 0));
        _udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 16);
        if (_local is not null)
            _udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, _local.GetAddressBytes());
    }

    protected override void SendCore(ReadOnlySpan<byte> data)
    {
        var packet = SacnPacket.BuildData(_sacnUniverse, data, _sequence++, SourceCid, "LuxEngine");
        _udp!.Send(packet, packet.Length, _target);
    }

    protected override void CloseCore()
    {
        _udp?.Dispose();
        _udp = null;
    }
}
