namespace LibreKO.Network.Requests;

public interface IMiningRequests
{
    void Start();
    void Attempt();
    void Stop();
    void FishingStart();
    void FishingAttempt();
    void FishingStop();
    void YutBet();
}

public sealed class MiningRequests : IMiningRequests
{
    private readonly IPacketSink sink;

    public MiningRequests(IPacketSink sink) => this.sink = sink;

    public void Start() => SendMiningSub(MiningSubOpcode.MiningStart);

    public void Attempt() => SendMiningSub(MiningSubOpcode.MiningAttempt);

    public void Stop() => SendMiningSub(MiningSubOpcode.MiningStop);

    public void FishingStart() => SendMiningSub(MiningSubOpcode.FishingStart);

    public void FishingAttempt() => SendMiningSub(MiningSubOpcode.FishingAttempt);

    public void FishingStop() => SendMiningSub(MiningSubOpcode.FishingStop);

    public void YutBet()
    {
        var p = new Packet(GameOpcodes.GS_MINING);
        p.WriteByte((byte)MiningSubOpcode.BettingGame);
        p.WriteByte((byte)YutBetKind.Coins);
        sink.Send(p);
    }

    private void SendMiningSub(MiningSubOpcode sub)
    {
        var p = new Packet(GameOpcodes.GS_MINING);
        p.WriteByte((byte)sub);
        sink.Send(p);
    }
}
