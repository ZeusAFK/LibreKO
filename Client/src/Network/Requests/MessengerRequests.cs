namespace LibreKO.Network.Requests;

public interface IMessengerRequests
{
    void List();
    void Message(string toName, string text);
}

public sealed class MessengerRequests : IMessengerRequests
{
    private readonly IPacketSink sink;

    public MessengerRequests(IPacketSink sink) => this.sink = sink;

    public void List()
    {
        var p = new Packet(GameOpcodes.GS_MESSENGER);
        p.WriteByte(1);
        sink.Send(p);
    }

    public void Message(string toName, string text)
    {
        if (string.IsNullOrEmpty(toName) || string.IsNullOrEmpty(text)) return;
        if (toName.Length > 20) toName = toName.Substring(0, 20);
        if (text.Length > 128) text = text.Substring(0, 128);
        var p = new Packet(GameOpcodes.GS_MESSENGER);
        p.WriteByte(2);
        p.WriteSByteString(toName);
        p.WriteSByteString(text);
        sink.Send(p);
    }
}
