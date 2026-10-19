namespace LibreKO.Network.Readers;

internal sealed class SiegeReader : IPacketReader
{
    private readonly HostilityRules hostility;
    private readonly ProgressSession progress;
    private readonly SiegeSession siege;

    public SiegeReader(HostilityRules hostility, ProgressSession progress, SiegeSession siege)
    {
        this.hostility = hostility;
        this.progress = progress;
        this.siege = siege;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_SIEGE, HandleSiege);
    }

    private void HandleSiege(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        switch ((SiegeWarSubOpcode)p.ReadByte())
        {
            case (SiegeWarSubOpcode)SiegeWarSubOpcode.CastleGuard:
                HandleSiegeGuard(p);
                break;
            case (SiegeWarSubOpcode)SiegeWarSubOpcode.CastleOffice:
                HandleSiegeOffice(p);
                break;
            case SiegeWarSubOpcode.Base:
                if (p.RemainingBytes >= 1 + sizeof(short) && (SiegeWarSubOpcode)p.ReadByte() == SiegeWarSubOpcode.Base)
                    siege.RaiseSiegeBase(p.ReadShort());
                break;
            case SiegeWarSubOpcode.Status:
                if (SiegeWarStatus.Read(p) is { } status)
                {
                    siege.ApplySiegeWar(status);
                    hostility.RefreshKnownHostility();
                    siege.RaiseSiegeWar();
                }
                break;
            case SiegeWarSubOpcode.Points:
                if (SiegePoints.Read(p, siege.SiegePointTotal) is { } points)
                {
                    siege.ApplyPoints(points);
                }
                break;
        }
    }

    private void HandleSiegeGuard(Packet p)
    {
        switch ((SiegeGuardSubOpcode)KingWire.U8(p))
        {
            case SiegeGuardSubOpcode.Open:
                siege.RaiseSiegeGuard();
                break;
            case SiegeGuardSubOpcode.Schedule:
                siege.RaiseSiegeSchedule(SiegeWire.ReadSchedule(p));
                break;
            case SiegeGuardSubOpcode.Apply:
                siege.RaiseSiegeApply(KingWire.I16(p));
                break;
            case SiegeGuardSubOpcode.Challengers:
                siege.RaiseSiegeChallengers(SiegeWire.ReadChallengers(p));
                break;
            case SiegeGuardSubOpcode.Defenders:
                siege.RaiseSiegeDefenders(SiegeWire.ReadDefenders(p));
                break;
        }
    }

    private void HandleSiegeOffice(Packet p)
    {
        var sub = (SiegeOfficeSubOpcode)KingWire.U8(p);
        switch (sub)
        {
            case SiegeOfficeSubOpcode.Open:
                siege.RaiseSiegeOffice(SiegeWire.ReadOffice(p));
                break;
            case SiegeOfficeSubOpcode.Collect:
                var collected = SiegeWire.ReadCollected(p);
                if (collected.Result == SiegeWire.Success) progress.ApplyGold(collected.Coins);
                siege.RaiseSiegeCollected(collected);
                break;
            case SiegeOfficeSubOpcode.TaxList:
                siege.RaiseSiegeTaxRates(SiegeWire.ReadTaxRates(p));
                break;
            case SiegeOfficeSubOpcode.MoradonRate:
            case SiegeOfficeSubOpcode.DelosRate:
            case SiegeOfficeSubOpcode.DungeonFee:
                siege.RaiseSiegeRateChanged(SiegeWire.ReadRateChanged(p, sub));
                break;
        }
    }
}
