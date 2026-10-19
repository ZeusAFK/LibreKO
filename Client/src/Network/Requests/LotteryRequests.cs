namespace LibreKO.Network.Requests;

public interface ILotteryRequests
{
    void StateRequest();
    void BuyTicket();
}

public sealed class LotteryRequests : ILotteryRequests
{
    private readonly IPacketSink sink;

    public LotteryRequests(IPacketSink sink) => this.sink = sink;

    public void StateRequest()
    {
        var packet = new Packet(GameOpcodes.GS_LOTTERY);
        packet.WriteByte((byte)LotterySubOpcode.State);
        sink.Send(packet);
    }

    public void BuyTicket()
    {
        var packet = new Packet(GameOpcodes.GS_LOTTERY);
        packet.WriteByte((byte)LotterySubOpcode.Join);
        sink.Send(packet);
    }
}
