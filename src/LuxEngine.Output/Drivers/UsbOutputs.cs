using System.IO.Ports;
using LuxEngine.Output.Protocols;

namespace LuxEngine.Output.Drivers;

/// <summary>
/// Enttec DMX USB Pro (and compatible widgets such as DMXking ultraDMX). The widget shows up in
/// Windows as a COM port once the FTDI driver is installed; it generates DMX timing itself.
/// </summary>
public sealed class EnttecProOutput : DmxOutputBase
{
    private readonly string _portName;
    private SerialPort? _port;

    public EnttecProOutput(int universe, string portName) : base(universe) => _portName = portName;

    public override string Description => $"Enttec DMX USB Pro on {_portName}";

    protected override void OpenCore()
    {
        if (string.IsNullOrWhiteSpace(_portName)) throw new InvalidOperationException("Choose the COM port of the interface.");
        _port = new SerialPort(_portName, 57600, Parity.None, 8, StopBits.One) { WriteTimeout = 500 };
        _port.Open();
    }

    protected override void SendCore(ReadOnlySpan<byte> data)
    {
        var message = EnttecProPacket.BuildDmx(data);
        _port!.Write(message, 0, message.Length);
    }

    protected override void CloseCore()
    {
        _port?.Dispose();
        _port = null;
    }
}

/// <summary>
/// Enttec Open DMX USB (and other "dumb" FTDI cables). The computer generates the DMX signal itself:
/// a break, then the start code and 512 slots at 250 kbaud, 8N2.
/// </summary>
public sealed class OpenDmxOutput : DmxOutputBase
{
    private readonly string _portName;
    private readonly byte[] _buffer = new byte[513];
    private SerialPort? _port;

    public OpenDmxOutput(int universe, string portName) : base(universe) => _portName = portName;

    public override string Description => $"Open DMX USB on {_portName}";

    protected override void OpenCore()
    {
        if (string.IsNullOrWhiteSpace(_portName)) throw new InvalidOperationException("Choose the COM port of the interface.");
        _port = new SerialPort(_portName, 250000, Parity.None, 8, StopBits.Two) { WriteTimeout = 500, Handshake = Handshake.None };
        _port.Open();
    }

    protected override void SendCore(ReadOnlySpan<byte> data)
    {
        _buffer[0] = 0; // start code
        data[..Math.Min(512, data.Length)].CopyTo(_buffer.AsSpan(1));
        _port!.BreakState = true;   // break: at least 92 µs
        Thread.Sleep(1);
        _port.BreakState = false;   // mark after break
        _port.Write(_buffer, 0, _buffer.Length);
    }

    protected override void CloseCore()
    {
        _port?.Dispose();
        _port = null;
    }
}
