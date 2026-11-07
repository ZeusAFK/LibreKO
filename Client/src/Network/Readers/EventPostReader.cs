namespace LibreKO.Network.Readers;

internal sealed class EventPostReader : IPacketReader
{
    private readonly EventPostSession eventPost;

    public EventPostReader(EventPostSession eventPost)
    {
        this.eventPost = eventPost;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_EVENT_BOARD, EventBoardSubOpcode.PostBoard, HandleBoard);
        dispatcher.On(GameOpcodes.GS_EVENT_BOARD, EventBoardSubOpcode.PostRegister, HandleRegistered);
        dispatcher.On(GameOpcodes.GS_EVENT_BOARD, EventBoardSubOpcode.PostHistory, HandleHistory);
    }

    private void HandleBoard(Packet p)
    {
        if (EventPostWire.ReadReply(p) is { } reply) eventPost.ApplyBoard(reply);
    }

    private void HandleRegistered(Packet p)
    {
        if (EventPostWire.ReadReply(p) is { } reply) eventPost.ApplyRegistered(reply);
    }

    private void HandleHistory(Packet p)
    {
        if (EventPostWire.ReadHistory(p) is { } history) eventPost.ApplyHistory(history);
    }
}
