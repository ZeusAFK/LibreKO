namespace LibreKO.Network.Requests;

public interface IEventPostRequests
{
    void Board(EventPostEvent eventId);
    void Post(EventPostEvent eventId, string text);
    void History();
}

public sealed class EventPostRequests : IEventPostRequests
{
    private readonly IPacketSink sink;

    public EventPostRequests(IPacketSink sink) => this.sink = sink;

    public void Board(EventPostEvent eventId)
    {
        var p = new Packet(GameOpcodes.GS_EVENT_BOARD);
        p.WriteByte((byte)EventBoardSubOpcode.PostBoard);
        p.WriteUInt((uint)eventId);
        sink.Send(p);
    }

    public void Post(EventPostEvent eventId, string text)
    {
        var p = new Packet(GameOpcodes.GS_EVENT_BOARD);
        p.WriteByte((byte)EventBoardSubOpcode.PostRegister);
        p.WriteUInt((uint)eventId);
        p.WriteSByteString(text);
        sink.Send(p);
    }

    public void History()
    {
        var p = new Packet(GameOpcodes.GS_EVENT_BOARD);
        p.WriteByte((byte)EventBoardSubOpcode.PostHistory);
        sink.Send(p);
    }
}
