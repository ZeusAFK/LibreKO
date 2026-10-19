namespace LibreKO.Network.Readers;

internal sealed class BattleEventReader : IPacketReader
{
    private readonly BattleEventSession battleEvent;
    private readonly SelfSession self;

    public BattleEventReader(BattleEventSession battleEvent, SelfSession self)
    {
        this.battleEvent = battleEvent;
        this.self = self;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_BATTLE_EVENT, HandleBattleEvent);
        dispatcher.On(GameOpcodes.GS_MAP_EVENT, HandleMapEvent);
    }

    private void HandleBattleEvent(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (BattleEventSubOpcode)p.ReadByte();
        switch (sub)
        {
            case BattleEventSubOpcode.ClanMatch:
                HandleClanMatch(p);
                break;
            case BattleEventSubOpcode.Open:
                {
                    if (p.RemainingBytes < 2) return;
                    var a = (BattleEventNotice)p.ReadByte();
                    int zone = p.ReadByte();
                    if (p.RemainingBytes >= 4)
                    {
                        int remaining = p.ReadInt();
                        battleEvent.RaiseBattleStatus((BattleEventState)(int)a, zone, remaining);
                    }
                    else
                    {
                        battleEvent.RaiseBattleZoneToggle(a, zone);
                    }
                    break;
                }
            case BattleEventSubOpcode.RankBoard:
                battleEvent.RaiseRankBoard(ReadRankBoard(p));
                break;
            case BattleEventSubOpcode.Result:
                {
                    if (p.RemainingBytes < 2) return;
                    var declareType = (BattleEventNotice)p.ReadByte();
                    int nation = p.ReadByte();
                    battleEvent.RaiseBattleResult(declareType, nation);
                    break;
                }
        }
    }

    private static List<RankBoardRow> ReadRankBoard(Packet p)
    {
        var rows = new List<RankBoardRow>();
        int count = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        for (int i = 0; i < count && p.RemainingBytes >= 2; i++)
        {
            int slot = p.ReadUShort();
            string name = p.ReadString();
            p.ReadUShort();
            int clanId = p.ReadShort();
            int markVersion = p.ReadShort();
            string clanName = p.ReadString();
            int tail = p.ReadShort();
            if (RankBoard.Row(slot, name, clanId, markVersion, clanName, tail) is { } row) rows.Add(row);
        }
        return rows;
    }

    private void HandleClanMatch(Packet p)
    {
        if (p.RemainingBytes < 1 || !Domain.ClanMatch.ShowsPlate(self.LastEnter.Zone)) return;
        switch ((ClanMatchKind)p.ReadByte())
        {
            case ClanMatchKind.Result when p.RemainingBytes >= 2:
                battleEvent.RaiseClanMatchResult(p.ReadShort());
                break;
            case ClanMatchKind.Roster:
                battleEvent.RaiseClanMatchRoster(ClanMatchReply.ReadRoster(p));
                break;
            case ClanMatchKind.Vitals:
                if (ClanMatchReply.ReadVitals(p) is { } vitals && vitals.Id >= 0) battleEvent.RaiseClanMatchVitals(vitals);
                break;
        }
    }

    private void HandleMapEvent(Packet p)
    {
        if (MapEventReply.Read(p) is not { } reply) return;
        switch (reply.Kind)
        {
            case MapEventKind.Owners:
                battleEvent.ApplyMonumentOwners(reply.Owners);
                break;
            case MapEventKind.Captured:
                if (reply.Index >= battleEvent.MonumentOwners.Count) return;
                battleEvent.ApplyMonumentCaptured(reply.Index, (byte)reply.Nation);
                break;
            case MapEventKind.Points:
                battleEvent.RaiseBattleScore(MapEventSubOpcode.Points, (int)reply.A, (int)reply.B);
                break;
            case MapEventKind.Cont:
                battleEvent.ApplyNereidCont(reply.A);
                break;
        }
        battleEvent.RaiseMapEvent(reply);
    }
}
