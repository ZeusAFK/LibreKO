namespace LibreKO.Network.Readers;

internal sealed class ClanBattleReader : IPacketReader
{
    private readonly ClanBattleSession clanBattle;

    private const byte IdentityCheckResult = 1;

    public ClanBattleReader(ClanBattleSession clanBattle)
    {
        this.clanBattle = clanBattle;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_CLAN_BATTLE, HandleClanBattle);
        dispatcher.On(GameOpcodes.GS_CLANPOINTS_BATTLE, HandleIdentityCheck);
    }

    private void HandleClanBattle(Packet p)
    {
        clanBattle.RaiseClanBattleNotify();
    }

    private void HandleIdentityCheck(Packet p)
    {
        if (p.RemainingBytes < 2 || p.ReadByte() != IdentityCheckResult) return;
        clanBattle.RaiseIdentityCheckResult(p.ReadByte());
    }
}
