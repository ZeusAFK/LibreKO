namespace LibreKO.Network.Readers;

internal sealed class MonsterStoneReader : IPacketReader
{
    private readonly BifrostSession bifrost;
    private readonly ChaosSession chaos;
    private readonly MonsterStoneSession monsterStone;
    private readonly Func<ulong> nowMsec;

    private const ushort NestFinishEvent = 17;
    private const ulong MsPerSecond = 1000UL;

    public MonsterStoneReader(BifrostSession bifrost, ChaosSession chaos, MonsterStoneSession monsterStone, Func<ulong> nowMsec)
    {
        this.bifrost = bifrost;
        this.chaos = chaos;
        this.monsterStone = monsterStone;
        this.nowMsec = nowMsec;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_BIFROST, TempleSubOpcode.MonsterSquad, HandleNestTimer);
        dispatcher.On(GameOpcodes.GS_EVENT, TempleSubOpcode.MonsterStone, HandleMonsterStone);
        dispatcher.On(GameOpcodes.GS_EVENT, TempleSubOpcode.TempleEventFinish, HandleTempleEventFinish);
    }

    private void HandleMonsterStone(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var result = (MonsterStoneResult)p.ReadByte();
        int itemId = result == MonsterStoneResult.Entered && p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        if (result == MonsterStoneResult.Entered) monsterStone.ApplyEntered(itemId);
        monsterStone.RaiseMonsterStone(result, itemId);
    }

    private void HandleNestTimer(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        int seconds = p.ReadShort();
        if (seconds < 0) return;
        monsterStone.StartNestTimer((uint)seconds, nowMsec(), completed: false);
        bifrost.BorderWar.RoomClock(seconds, DateTime.UtcNow);
    }

    private void HandleTempleEventFinish(Packet p)
    {
        if (p.RemainingBytes < 7) return;
        ushort eventId = p.ReadUShort();
        byte winner = p.ReadByte();
        uint seconds = p.ReadUInt();
        if (TryHandleChaosFinish(eventId, seconds)) return;
        if (eventId != NestFinishEvent)
        {
            bifrost.BorderWar.Finish(winner, seconds, DateTime.UtcNow);
            bifrost.RaiseTempleEventFinish(eventId, winner, seconds);
            return;
        }
        monsterStone.StartNestTimer(seconds, nowMsec(), completed: true);
    }

    private bool TryHandleChaosFinish(ushort eventId, uint seconds)
    {
        if (eventId != ChaosBattle.IndunEvent) return false;
        chaos.Finish(nowMsec() + seconds * MsPerSecond, seconds);
        return true;
    }
}
