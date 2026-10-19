namespace LibreKO.Network.Readers;

internal sealed class ProgressReader : IPacketReader
{
    private readonly GameSession session;
    private readonly ChaosSession chaos;
    private readonly ProgressSession progress;
    private readonly SelfSession self;

    public ProgressReader(GameSession session, ChaosSession chaos, ProgressSession progress, SelfSession self)
    {
        this.session = session;
        this.chaos = chaos;
        this.progress = progress;
        this.self = self;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_LEVEL_CHANGE, HandleLevelChange);
        dispatcher.On(GameOpcodes.GS_LOYALTY_CHANGE, HandleLoyaltyChange);
        dispatcher.On(GameOpcodes.GS_WEIGHT_CHANGE, HandleWeightChange);
        dispatcher.On(GameOpcodes.GS_STATE_CHANGE, HandleStateChange);
    }

    private void HandleLevelChange(Packet p)
    {
        if (p.RemainingBytes < 5) return;
        int charId = p.ReadInt();
        if (charId != self.MyCharId)
        {
            progress.RaisePeerLevelChange(charId, p.ReadByte());
            return;
        }
        int level = p.ReadByte();
        int statPoints = p.ReadShort();
        int skillPool = p.ReadByte();
        long maxExp = p.ReadLong();
        long exp = p.ReadLong();
        int maxHp = p.ReadShort(); int hp = p.ReadShort();
        int maxMp = p.ReadShort(); int mp = p.ReadShort();
        if (p.RemainingBytes >= 4) self.Sheet.SetMaxWeight(p.ReadInt());
        progress.ApplyLevel(level, statPoints, skillPool, maxExp, exp, maxHp, hp, maxMp, mp);
    }

    private void HandleLoyaltyChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var type = (LoyaltySubOpcode)p.ReadByte();
        if (type == LoyaltySubOpcode.Manner)
        {
            if (p.RemainingBytes >= sizeof(int)) progress.ApplyManner(p.ReadInt());
            return;
        }
        if (type != LoyaltySubOpcode.Totals) return;
        int loyalty = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        int monthly = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        progress.ApplyLoyalty(loyalty, monthly);
    }

    private void HandleWeightChange(Packet p)
    {
        if (p.RemainingBytes < 4) return;
        progress.RaiseWeightChange(p.ReadInt());
    }

    private void HandleStateChange(Packet p)
    {
        if (p.RemainingBytes < 5) return;
        int charId = p.ReadInt();
        int type = p.ReadByte();
        int value = p.RemainingBytes >= 4 ? p.ReadInt()
                  : p.RemainingBytes >= 1 ? p.ReadByte()
                  : 0;
        if (charId == self.MyCharId) progress.ApplyOwnStateChange(type, value);
        if (type == StateChange.Title) session.TitleChanged(charId, value);
        NoteChaosState(charId, type, value);
        progress.RaiseStateChange(charId, type, value);
    }

    private void NoteChaosState(int charId, int type, int value)
    {
        if (type != StateChange.Abnormal || value != SizeState.ChaosNormal || self.LastEnter.Zone != ChaosBattle.Zone || chaos.ChaosStarted)
            return;
        chaos.Start();
    }
}
