namespace LibreKO.Network.Requests;

public interface IUserOptionRequests
{
    void SetOwnRankAuraShown(bool shown);
    void TradeState(bool blocked);
}

public sealed class UserOptionRequests : IUserOptionRequests
{
    private readonly IPacketSink sink;
    private readonly UserOptionSession options;
    private readonly SelfSession self;

    public UserOptionRequests(IPacketSink sink, UserOptionSession options, SelfSession self)
    {
        this.sink = sink;
        this.options = options;
        this.self = self;
    }

    public void SetOwnRankAuraShown(bool shown)
    {
        if (options.OwnRankAuraShown == shown) return;
        options.ApplyOwnRankAuraShown(shown);
        var p = new Packet(GameOpcodes.GS_USER_OPTION);
        p.WriteByte((byte)UserOptionSubOpcode.RankAura);
        p.WriteByte((byte)(shown ? 1 : 0));
        sink.Send(p);
        options.RaiseRankAuraShown(self.MyCharId, shown);
    }

    public void TradeState(bool blocked)
    {
        var p = new Packet(GameOpcodes.GS_USER_OPTION);
        p.WriteByte((byte)UserOptionSubOpcode.TradeState);
        p.WriteByte((byte)(blocked ? 1 : 0));
        sink.Send(p);
    }
}
