namespace LuxEngine.Output.Protocols;

/// <summary>Builds messages for the Enttec DMX USB Pro widget API.</summary>
public static class EnttecProPacket
{
    public const byte StartOfMessage = 0x7E;
    public const byte EndOfMessage = 0xE7;
    public const byte LabelOutputOnlySendDmx = 6;

    public static byte[] BuildDmx(ReadOnlySpan<byte> data)
    {
        int slots = Math.Min(data.Length, 512);
        int length = slots + 1; // + DMX start code
        var message = new byte[length + 5];
        message[0] = StartOfMessage;
        message[1] = LabelOutputOnlySendDmx;
        message[2] = (byte)(length & 0xFF);
        message[3] = (byte)(length >> 8);
        message[4] = 0x00; // DMX start code
        data[..slots].CopyTo(message.AsSpan(5));
        message[^1] = EndOfMessage;
        return message;
    }
}
