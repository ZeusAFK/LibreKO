using System;
using LibreKO.Domain;

namespace LibreKO.Network;

public partial class Net
{
    public event Action? SiegeGuardEvent;
    public event Action<SiegeSchedule>? SiegeScheduleEvent;
    public event Action<short>? SiegeApplyEvent;
    public event Action<SiegeChallengers>? SiegeChallengersEvent;
    public event Action<SiegeDefenders>? SiegeDefendersEvent;
    public event Action<SiegeOffice>? SiegeOfficeEvent;
    public event Action<SiegeCollected>? SiegeCollectedEvent;
    public event Action<SiegeTaxRates>? SiegeTaxRatesEvent;
    public event Action<SiegeRateChanged>? SiegeRateChangedEvent;

    private void HandleSiege(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        switch (p.ReadByte())
        {
            case SiegeWarfare.CastleGuard:
                HandleSiegeGuard(p);
                break;
            case SiegeWarfare.CastleOffice:
                HandleSiegeOffice(p);
                break;
        }
    }

    private void HandleSiegeGuard(Packet p)
    {
        switch (KingWire.U8(p))
        {
            case SiegeWarfare.GuardOpen:
                SiegeGuardEvent?.Invoke();
                break;
            case SiegeWarfare.Schedule:
                SiegeScheduleEvent?.Invoke(SiegeWire.ReadSchedule(p));
                break;
            case SiegeWarfare.Apply:
                SiegeApplyEvent?.Invoke(KingWire.I16(p));
                break;
            case SiegeWarfare.Challengers:
                SiegeChallengersEvent?.Invoke(SiegeWire.ReadChallengers(p));
                break;
            case SiegeWarfare.Defenders:
                SiegeDefendersEvent?.Invoke(SiegeWire.ReadDefenders(p));
                break;
        }
    }

    private void HandleSiegeOffice(Packet p)
    {
        byte sub = KingWire.U8(p);
        switch (sub)
        {
            case SiegeWarfare.OfficeOpen:
                SiegeOfficeEvent?.Invoke(SiegeWire.ReadOffice(p));
                break;
            case SiegeWarfare.Collect:
                SiegeCollectedEvent?.Invoke(SiegeWire.ReadCollected(p));
                break;
            case SiegeWarfare.TaxList:
                SiegeTaxRatesEvent?.Invoke(SiegeWire.ReadTaxRates(p));
                break;
            case SiegeWarfare.MoradonRate:
            case SiegeWarfare.DelosRate:
            case SiegeWarfare.DungeonFee:
                SiegeRateChangedEvent?.Invoke(SiegeWire.ReadRateChanged(p, sub));
                break;
        }
    }

    public void SendSiegeSchedule() => _conn.Send(SiegeWire.Request(SiegeWarfare.CastleGuard, SiegeWarfare.Schedule));

    public void SendSiegeAssault() => _conn.Send(SiegeWire.Request(SiegeWarfare.CastleGuard, SiegeWarfare.Assault));

    public void SendSiegeChallengers() =>
        _conn.Send(SiegeWire.Request(SiegeWarfare.CastleGuard, SiegeWarfare.Challengers));

    public void SendSiegeDefenders() => _conn.Send(SiegeWire.Request(SiegeWarfare.CastleGuard, SiegeWarfare.Defenders));

    public void SendSiegeApply(bool join)
    {
        _conn.Send(SiegeWire.Request(SiegeWarfare.CastleGuard, SiegeWarfare.Apply,
            join ? SiegeWarfare.ApplyJoin : SiegeWarfare.ApplyCancel));
        SendSiegeChallengers();
    }

    public void SendSiegeCollect() => _conn.Send(SiegeWire.Request(SiegeWarfare.CastleOffice, SiegeWarfare.Collect));

    public void SendSiegeTaxList() => _conn.Send(SiegeWire.Request(SiegeWarfare.CastleOffice, SiegeWarfare.TaxList));

    public void SendSiegeRate(SiegeRateKind kind, int value) => _conn.Send(SiegeWire.Rate(kind, value));
}
