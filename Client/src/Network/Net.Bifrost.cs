using System;
using LibreKO.Domain;

namespace LibreKO.Network;

public partial class Net
{
    private const byte BifrostEventSub = 2;

    private const byte BifrostJoinSub    = 8;
    private const byte BifrostDisbandSub = 9;
    private const byte TempleSignUpsSub  = 16;
    private const byte TempleScreenSub   = 3;
    private const byte AltarFlagSub      = 49;
    private const byte AltarTimerSub     = 50;
    private const byte DrakiTimerSub     = 35;
    private const byte DrakiTimerEndSub  = 36;
    private const int DrakiTimerHeaderBytes = 2;

    private ulong _drakiEndsAtMs;
    public int DrakiStage { get; private set; }
    public int DrakiSubStage { get; private set; }

    public int DrakiSecondsLeft
    {
        get
        {
            ulong now = Godot.Time.GetTicksMsec();
            return _drakiEndsAtMs > now ? (int)((_drakiEndsAtMs - now + 999) / 1000) : 0;
        }
    }

    public void ClearDrakiTimer() => _drakiEndsAtMs = 0;

    public event Action<int, TempleEventType>? BifrostTimeEvent;

    public event Action<byte, int>? BifrostJoinEvent;

    public event Action<int, int, int>? TempleSignUpsEvent;

    public event Action? BifrostDisbandEvent;

    public BorderWarState BorderWar { get; } = new();

    public TempleSignUpBoard SignUps { get; } = new();

    public event Action<int, int>? TempleScreenScoreEvent;
    public event Action<int>? AltarTimerEvent;
    public event Action<int, int, uint>? TempleEventFinishEvent;
    public event Action<string, byte>? AltarFlagEvent;

    private void HandleBifrost(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == MonsterSquadSub)
        {
            HandleNestTimer(p);
            return;
        }
        if (sub != BifrostEventSub) return;

        int remaining = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        if (remaining < 0) remaining = 0;
        var eventType = p.RemainingBytes >= 1 ? (TempleEventType)p.ReadByte() : TempleEventType.None;
        BifrostTimeEvent?.Invoke(remaining, eventType);
    }

    public event Action<byte>? FullMoonEntryEvent;
    public event Action<byte>? FullMoonTowerEvent;
    public event Action<byte, byte>? FullMoonStageEvent;
    public int[]? FullMoonHotbar { get; set; }

    public void SendFullMoonEnter()
    {
        var p = new Packet(GameOpcodes.GS_EVENT);
        p.WriteByte(Domain.FullMoon.EntrySub);
        p.WriteByte(Domain.FullMoon.EntryKeyA);
        p.WriteByte(Domain.FullMoon.EntryKeyB);
        _conn.Send(p);
    }

    private void HandleBifrostEvent(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case MonsterStoneSub:
                HandleMonsterStone(p);
                break;
            case Domain.FullMoon.EntrySub:
                if (p.RemainingBytes >= 1) FullMoonEntryEvent?.Invoke(p.ReadByte());
                break;
            case Domain.FullMoon.TowerSub:
                if (p.RemainingBytes >= 1) FullMoonTowerEvent?.Invoke(p.ReadByte());
                break;
            case Domain.FullMoon.StageSub:
                if (p.RemainingBytes >= 2) FullMoonStageEvent?.Invoke(p.ReadByte(), p.ReadByte());
                break;
            case TempleEventFinishSub:
                HandleTempleEventFinish(p);
                break;
            case BifrostJoinSub:
            {
                byte result = p.RemainingBytes >= 1 ? p.ReadByte() : TempleJoin.NotOpen;
                int zone = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                BifrostJoinEvent?.Invoke(result, zone);
                break;
            }
            case TempleSignUpsSub:
            {
                if (p.RemainingBytes < 6) break;
                int eventId = p.ReadUShort();
                int karus = p.ReadUShort();
                int elMorad = p.ReadUShort();
                TempleSignUpsEvent?.Invoke(eventId, karus, elMorad);
                break;
            }
            case BifrostDisbandSub:
            {
                if (p.RemainingBytes >= 1) p.ReadByte();
                if (p.RemainingBytes >= 2) p.ReadShort();
                BifrostDisbandEvent?.Invoke();
                break;
            }
            case TempleScreenSub:
            {
                int karus = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                int elmo = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                BorderWar.SetScores(karus, elmo);
                TempleScreenScoreEvent?.Invoke(karus, elmo);
                break;
            }
            case AltarFlagSub:
            {
                string name = p.RemainingBytes >= 1 ? p.ReadSByteString() : string.Empty;
                byte nation = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                BorderWar.FragmentTaken(name, nation);
                AltarFlagEvent?.Invoke(name, nation);
                break;
            }
            case AltarTimerSub:
            {
                int secs = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                BorderWar.AltarTimer(secs, DateTime.UtcNow);
                AltarTimerEvent?.Invoke(secs);
                break;
            }
            case DrakiTimerSub:
            {
                if (p.RemainingBytes < DrakiTimerHeaderBytes + 2 + 2 + 4 + 4) break;
                p.ReadUShort();
                DrakiStage = p.ReadUShort();
                DrakiSubStage = p.ReadUShort();
                int limit = p.ReadInt();
                int elapsed = p.ReadInt();
                int left = Math.Max(0, limit - elapsed);
                _drakiEndsAtMs = left > 0 ? Godot.Time.GetTicksMsec() + (ulong)left * 1000UL : 0;
                break;
            }
            case DrakiTimerEndSub:
                ClearDrakiTimer();
                break;
        }
    }

    public void SendBifrostTimeRequest()
    {
        var p = new Packet(GameOpcodes.GS_BIFROST);
        p.WriteByte(BifrostEventSub);
        _conn.Send(p);
    }

    public void SendBifrostJoin(TempleEventType type)
    {
        var p = new Packet(GameOpcodes.GS_EVENT);
        p.WriteByte(BifrostJoinSub);
        p.WriteByte((byte)type);
        _conn.Send(p);
    }

    public void SendBifrostDisband()
    {
        var p = new Packet(GameOpcodes.GS_EVENT);
        p.WriteByte(BifrostDisbandSub);
        _conn.Send(p);
    }
}
