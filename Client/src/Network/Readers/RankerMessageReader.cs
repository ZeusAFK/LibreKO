namespace LibreKO.Network.Readers;

internal sealed class RankerMessageReader : IPacketReader
{
    private readonly RankerMessageSession rankerMessage;

    public RankerMessageReader(RankerMessageSession rankerMessage)
    {
        this.rankerMessage = rankerMessage;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_RANKER_MESSAGE, HandleRankerMessage);
    }

    private void HandleRankerMessage(Packet p)
    {
        if (p.RemainingBytes < 3) return;
        var sub = (RankerMessageSub)p.ReadByte();
        short result = p.ReadShort();
        if (sub == RankerMessageSub.View && result == RankerMessage.Done)
        {
            string name = p.RemainingBytes >= 2 ? p.ReadString() : "";
            string message = p.RemainingBytes >= 2 ? p.ReadString() : "";
            rankerMessage.RaiseRankerPaper(name, message);
            return;
        }
        rankerMessage.RaiseRankerResult(sub, result);
    }
}
