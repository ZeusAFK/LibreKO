using System;

namespace LibreKO.Network;

public enum TempleEventType : byte
{
    None = 0,
    Chaos = 1,
    BorderDefenseWar = 2,
    JuraidMountain = 3,
}

public partial class Net
{
    private const byte BifrostEventSub = 2;

    private const byte BifrostJoinSub    = 8;
    private const byte BifrostDisbandSub = 9;
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

    public event Action<bool, int>? BifrostJoinEvent;

    public event Action? BifrostDisbandEvent;

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

    private void HandleBifrostEvent(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case MonsterStoneSub:
                HandleMonsterStone(p);
                break;
            case TempleEventFinishSub:
                HandleTempleEventFinish(p);
                break;
            case BifrostJoinSub:
            {
                bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
                int zone = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                BifrostJoinEvent?.Invoke(ok, zone);
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
                TempleScreenScoreEvent?.Invoke(karus, elmo);
                break;
            }
            case AltarFlagSub:
            {
                string name = p.RemainingBytes >= 1 ? p.ReadSByteString() : string.Empty;
                byte nation = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                AltarFlagEvent?.Invoke(name, nation);
                break;
            }
            case AltarTimerSub:
            {
                int secs = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
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

    public void SendBifrostJoin()
    {
        var p = new Packet(GameOpcodes.GS_EVENT);
        p.WriteByte(BifrostJoinSub);
        _conn.Send(p);
    }

    public void SendBifrostDisband()
    {
        var p = new Packet(GameOpcodes.GS_EVENT);
        p.WriteByte(BifrostDisbandSub);
        _conn.Send(p);
    }
}
