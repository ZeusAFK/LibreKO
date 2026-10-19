namespace LibreKO.Network.Requests;

public interface IKnightRoyaleRequests
{
    void RoyaleSignUp(bool apply);
    void RoyaleTake(int boxId, int itemId, int index);
    void RoyaleDestroy(int bagSlot, int itemId);
}

public sealed class KnightRoyaleRequests : IKnightRoyaleRequests
{
    private readonly IPacketSink sink;

    public KnightRoyaleRequests(IPacketSink sink) => this.sink = sink;

    public void RoyaleSignUp(bool apply)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHT_ROYALE);
        p.WriteByte((byte)KnightRoyaleSubOpcode.SignUp);
        p.WriteByte((byte)(apply ? KnightRoyaleSignUpAction.Apply : KnightRoyaleSignUpAction.Cancel));
        sink.Send(p);
    }

    public void RoyaleTake(int boxId, int itemId, int index)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHT_ROYALE);
        p.WriteByte((byte)KnightRoyaleSubOpcode.Loot);
        p.WriteInt(boxId);
        p.WriteInt(itemId);
        p.WriteUShort((ushort)index);
        sink.Send(p);
    }

    public void RoyaleDestroy(int bagSlot, int itemId)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHT_ROYALE);
        p.WriteByte((byte)KnightRoyaleSubOpcode.Destroy);
        p.WriteByte((byte)bagSlot);
        p.WriteInt(itemId);
        sink.Send(p);
    }
}
