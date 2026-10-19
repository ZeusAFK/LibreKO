namespace LibreKO.Network.Readers;

internal sealed class ClanBattleWarReader : IPacketReader
{
    private readonly ClanBattleWarSession clanBattleWar;
    private readonly EntitiesSession entities;
    private readonly HostilityRules hostility;

    public ClanBattleWarReader(ClanBattleWarSession clanBattleWar, EntitiesSession entities, HostilityRules hostility)
    {
        this.clanBattleWar = clanBattleWar;
        this.entities = entities;
        this.hostility = hostility;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_KNIGHTS_PROCESS, KnightsSubOpcode.ClanBattleChallenge, HandleClanBattleResult);
        dispatcher.On(GameOpcodes.GS_KNIGHTS_PROCESS, KnightsSubOpcode.ClanBattleBegun, HandleClanBattleBegun);
        dispatcher.On(GameOpcodes.GS_KNIGHTS_PROCESS, KnightsSubOpcode.ClanBattleEnd, HandleClanBattleEnd);
    }

    private void HandleClanBattleResult(Packet p)
    {
        if (ClanBattleWire.ReadResult(p) is { } reply)
            clanBattleWar.RaiseClanBattleResult((ClanBattleResult)reply.Result, reply.ChallengerId, reply.Challenger);
    }

    private void HandleClanBattleBegun(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        SetEnemyClan(p.ReadShort());
        clanBattleWar.RaiseClanBattleBegun(clanBattleWar.EnemyClanId);
    }

    private void HandleClanBattleEnd(Packet p)
    {
        if (ClanBattleWire.ReadEnd(p) is not { } end) return;
        if (end.Kind == ClanBattleEnd.Cleared) SetEnemyClan(0);
        clanBattleWar.RaiseClanBattleEnd(end.Kind, end.Clan);
    }

    private void SetEnemyClan(short clanId)
    {
        clanBattleWar.ApplyEnemyClan(clanId);
        foreach (var known in entities.All)
            if (!known.IsNpc) known.Attackable = hostility.IsHostilePlayer(known);
    }
}
