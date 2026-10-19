namespace LibreKO.Network.Readers;

internal sealed class ClanBoardsReader : IPacketReader
{
    private readonly ClanBoardsSession clanBoards;

    private const byte PromoteBroadcast = 1;

    public ClanBoardsReader(ClanBoardsSession clanBoards)
    {
        this.clanBoards = clanBoards;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_KNIGHTS_PROCESS, KnightsSubOpcode.ClanAdvance, HandleClanAdvance);
        dispatcher.On(GameOpcodes.GS_KNIGHTS_PROCESS, KnightsSubOpcode.ClanAdvanceApply, HandleClanApplyResult);
        dispatcher.On(GameOpcodes.GS_KNIGHTS_PROCESS, KnightsSubOpcode.ClanAdvanceRegister, HandleClanAdvertResult);
        dispatcher.On(GameOpcodes.GS_KNIGHTS_PROCESS, KnightsSubOpcode.ClanAdvancePromote, HandleClanPromote);
        dispatcher.On(GameOpcodes.GS_KNIGHTS_PROCESS, KnightsSubOpcode.WishBoard, HandleWishBoard);
    }

    private void HandleClanAdvance(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var type = (ClanAdvanceType)p.ReadByte();
        if (type == ClanAdvanceType.Status && ClanBoardsWire.ReadBoardStatus(p) is { } status) clanBoards.RaiseClanBoardStatus(status);
        else if (type == ClanAdvanceType.Rows) clanBoards.RaiseClanAdverts(ClanBoardsWire.ReadAdverts(p));
    }

    private void HandleClanApplyResult(Packet p) => HandleClanKindResult(p, clanBoards.RaiseClanApplyResult);

    private void HandleClanAdvertResult(Packet p) => HandleClanKindResult(p, clanBoards.RaiseClanAdvertResult);

    private void HandleClanKindResult(Packet p, Action<int, int>? handler)
    {
        var kind = p.RemainingBytes >= 1 ? (ClanBoardAction)p.ReadByte() : ClanBoardAction.Register;
        int code = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        handler?.Invoke((int)kind, code);
    }

    private void HandleClanPromote(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        int code = p.ReadByte();
        if (code != PromoteBroadcast) { clanBoards.RaiseClanPromote(code, "", ""); return; }
        if (p.RemainingBytes < 3) return;
        p.ReadSByteString();
        string chief = p.ReadSByteString();
        string clan = p.ReadSByteString();
        clanBoards.RaiseClanPromote(code, chief, clan);
    }

    private void HandleWishBoard(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        switch ((WishBoardType)p.ReadByte())
        {
            case WishBoardType.Page when p.RemainingBytes >= 1:
                switch ((WishPageKind)p.ReadByte())
                {
                    case WishPageKind.Rows: clanBoards.RaiseWishPosts(ClanBoardsWire.ReadWishPosts(p)); break;
                    case WishPageKind.Status: clanBoards.RaiseWishStatus(ClanBoardsWire.ReadWishStatus(p)); break;
                    case WishPageKind.TooSoon: clanBoards.RaiseWishTooSoon(); break;
                }
                break;
            case WishBoardType.Requests: clanBoards.RaiseWishRequests(ClanBoardsWire.ReadWishRequests(p)); break;
            case WishBoardType.Intro: HandleClanKindResult(p, clanBoards.RaiseWishIntroResult); break;
            case WishBoardType.Request: HandleClanKindResult(p, clanBoards.RaiseWishRequestResult); break;
            case WishBoardType.Promote when p.RemainingBytes >= 1:
                {
                    int code = p.ReadByte();
                    clanBoards.RaiseWishPromote(code, code == PromoteBroadcast && p.RemainingBytes >= 1 ? p.ReadSByteString() : "");
                    break;
                }
        }
    }
}
