namespace LibreKO.Network.Requests;

public interface IShoutRequests
{
    void FullMoonShout(string message);
    void LogoShout(string message, byte r = 0xE0, byte g = 0xC0, byte b = 0x60);
}

public sealed class ShoutRequests : IShoutRequests
{
    private readonly IPacketSink sink;

    public ShoutRequests(IPacketSink sink) => this.sink = sink;

    public void FullMoonShout(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        var p = new Packet(GameOpcodes.GS_LOGOSSHOUT);
        p.WriteByte(Domain.FullMoon.ShoutKind);
        p.WriteInt(0);
        p.WriteSByteString(message.Length > ShoutWire.MaxLength ? message[..ShoutWire.MaxLength] : message);
        sink.Send(p);
    }

    public void LogoShout(string message, byte r = 0xE0, byte g = 0xC0, byte b = 0x60)
    {
        if (string.IsNullOrEmpty(message)) return;
        if (message.Length > ShoutWire.MaxLength) message = message.Substring(0, ShoutWire.MaxLength);

        var p = new Packet(GameOpcodes.GS_LOGOSSHOUT);
        p.WriteByte(0);
        p.WriteByte(r);
        p.WriteByte(g);
        p.WriteByte(b);
        p.WriteByte(0);
        p.WriteSByteString(message);
        sink.Send(p);
    }
}
