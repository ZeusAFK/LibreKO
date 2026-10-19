namespace LibreKO.Network.Requests;

public interface IRankerMessageRequests
{
    void RankerView(byte npcType);
    void RankerRedress(byte npcType);
    void RankerWrite(byte npcType, string message);
}

public sealed class RankerMessageRequests : IRankerMessageRequests
{
    private readonly IPacketSink sink;

    public RankerMessageRequests(IPacketSink sink) => this.sink = sink;

    public void RankerView(byte npcType) => SendRanker(RankerMessageSub.View, npcType);

    public void RankerRedress(byte npcType) => SendRanker(RankerMessageSub.Redress, npcType);

    public void RankerWrite(byte npcType, string message)
    {
        var p = new Packet(GameOpcodes.GS_RANKER_MESSAGE);
        p.WriteByte((byte)RankerMessageSub.Write);
        p.WriteByte(npcType);
        p.WriteString(message);
        sink.Send(p);
    }

    private void SendRanker(RankerMessageSub sub, byte npcType)
    {
        var p = new Packet(GameOpcodes.GS_RANKER_MESSAGE);
        p.WriteByte((byte)sub);
        p.WriteByte(npcType);
        sink.Send(p);
    }
}
