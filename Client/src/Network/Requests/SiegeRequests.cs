namespace LibreKO.Network.Requests;

public interface ISiegeRequests
{
    void Schedule();
    void Assault();
    void Challengers();
    void Defenders();
    void Apply(bool join);
    void Collect();
    void TaxList();
    void Rate(SiegeRateKind kind, int value);
    bool TowerBoard(int towerId);
    bool TowerUse(int npcId);
    bool TowerLeave(ushort x, ushort z);
    bool TowerExit();
}

public sealed class SiegeRequests : ISiegeRequests
{
    private readonly IPacketSink sink;
    private readonly SiegeSession siege;

    public SiegeRequests(IPacketSink sink, SiegeSession siege)
    {
        this.sink = sink;
        this.siege = siege;
    }

    public void Schedule() => sink.Send(SiegeWire.Guard(SiegeGuardSubOpcode.Schedule));

    public void Assault() => sink.Send(SiegeWire.Guard(SiegeGuardSubOpcode.Assault));

    public void Challengers() =>
            sink.Send(SiegeWire.Guard(SiegeGuardSubOpcode.Challengers));

    public void Defenders() => sink.Send(SiegeWire.Guard(SiegeGuardSubOpcode.Defenders));

    public void Apply(bool join)
    {
        var p = SiegeWire.Guard(SiegeGuardSubOpcode.Apply);
        p.WriteByte((byte)(join ? SiegeApplyAction.Join : SiegeApplyAction.Cancel));
        sink.Send(p);
        Challengers();
    }

    public void Collect() => sink.Send(SiegeWire.Office(SiegeOfficeSubOpcode.Collect));

    public void TaxList() => sink.Send(SiegeWire.Office(SiegeOfficeSubOpcode.TaxList));

    public void Rate(SiegeRateKind kind, int value) => sink.Send(SiegeWire.Rate(kind, value));

    public bool TowerBoard(int towerId) => SendSiegeTower(SiegeTowerSubOpcode.Board, p => p.WriteInt(towerId));

    public bool TowerUse(int npcId) => SendSiegeTower(SiegeTowerSubOpcode.Use, p => p.WriteInt(npcId));

    public bool TowerLeave(ushort x, ushort z) => SendSiegeTower(SiegeTowerSubOpcode.Leave, p =>
        {
            p.WriteUShort(x);
            p.WriteUShort(z);
        });

    public bool TowerExit() => SendSiegeTower(SiegeTowerSubOpcode.Exit, null);

    private bool SendSiegeTower(SiegeTowerSubOpcode sub, Action<Packet>? body)
    {
        if (siege.SiegeTowerPending) return false;
        var p = new Packet(GameOpcodes.GS_SIEGE_TOWER);
        p.WriteByte((byte)sub);
        body?.Invoke(p);
        sink.Send(p);
        siege.SiegeTowerPending = true;
        return true;
    }
}
