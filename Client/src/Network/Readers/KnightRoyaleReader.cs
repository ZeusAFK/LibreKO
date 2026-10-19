namespace LibreKO.Network.Readers;

internal sealed class KnightRoyaleReader : IPacketReader
{
    private readonly KnightRoyaleSession knightRoyale;
    private readonly Func<ulong> nowMsec;

    public KnightRoyaleReader(KnightRoyaleSession knightRoyale, Func<ulong> nowMsec)
    {
        this.knightRoyale = knightRoyale;
        this.nowMsec = nowMsec;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_KNIGHT_ROYALE, HandleKnightRoyale);
    }

    private void HandleKnightRoyale(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (KnightRoyaleSubOpcode)p.ReadByte();
        switch (sub)
        {
            case KnightRoyaleSubOpcode.SignUp:
                knightRoyale.RaiseRoyaleSignUp(KnightRoyaleWire.ReadSignUp(p));
                break;
            case KnightRoyaleSubOpcode.Entry:
                var entry = KnightRoyaleWire.ReadEntry(p);
                if (entry.Result != 1) break;
                knightRoyale.ApplyEntry(entry);
                break;
            case KnightRoyaleSubOpcode.Progress:
                HandleRoyaleProgress(p);
                break;
            case KnightRoyaleSubOpcode.Notice:
                HandleRoyaleNotice(p);
                break;
            case KnightRoyaleSubOpcode.Loot:
                knightRoyale.RaiseRoyaleLoot(KnightRoyaleWire.ReadLoot(p));
                break;
            case KnightRoyaleSubOpcode.ItemAdd:
                knightRoyale.RaiseRoyaleItem(KnightRoyaleWire.ReadItem(p));
                break;
            case KnightRoyaleSubOpcode.Restore:
                knightRoyale.ApplyRestore();
                break;
            case KnightRoyaleSubOpcode.Destroy:
                knightRoyale.RaiseRoyaleDestroy(p.RemainingBytes >= 1 && p.ReadByte() == 1);
                break;
        }
    }

    private void HandleRoyaleProgress(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var kind = (KnightRoyaleProgressKind)p.ReadByte();
        if (kind == KnightRoyaleProgressKind.Exp && p.RemainingBytes >= 2) knightRoyale.RaiseRoyaleExp(p.ReadShort());
        else if (kind == KnightRoyaleProgressKind.Stats) knightRoyale.RaiseRoyaleStats(KnightRoyaleWire.ReadStats(p));
    }

    private void HandleRoyaleNotice(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var kind = (KnightRoyaleNoticeKind)p.ReadByte();
        switch (kind)
        {
            case KnightRoyaleNoticeKind.Abandoned:
                var (name, survivors) = KnightRoyaleWire.ReadNamed(p);
                knightRoyale.ApplyAbandoned(name, survivors);
                break;
            case KnightRoyaleNoticeKind.Gas:
                if (p.RemainingBytes < 2) break;
                int released = p.ReadByte();
                int minutes = p.ReadByte();
                knightRoyale.ApplyGas(released, minutes, nowMsec());
                break;
            case KnightRoyaleNoticeKind.Chest:
            case KnightRoyaleNoticeKind.Delegate:
                var (x, z) = KnightRoyaleWire.ReadSpot(p);
                knightRoyale.RaiseRoyalePing(x, z, kind == KnightRoyaleNoticeKind.Delegate);
                break;
            case KnightRoyaleNoticeKind.Reward:
                knightRoyale.RaiseRoyaleReward(KnightRoyaleWire.ReadReward(p));
                break;
            case KnightRoyaleNoticeKind.Winner:
                var (winner, area) = KnightRoyaleWire.ReadNamed(p);
                knightRoyale.RaiseRoyaleWinner(winner, area);
                break;
            case KnightRoyaleNoticeKind.Survivors:
                if (p.RemainingBytes >= 1) knightRoyale.ApplySurvivors(p.ReadByte());
                knightRoyale.RaiseRoyaleAbandoned("", knightRoyale.RoyaleSurvivors);
                break;
        }
    }
}
