using System;
using Godot;
using LibreKO.Domain;

namespace LibreKO.Network;

public partial class Net
{
    private const byte MonsterStoneSub = 6;
    private const byte MonsterSquadSub = 5;
    private const byte TempleEventFinishSub = 10;
    private const ushort NestFinishEvent = 17;

    public event Action<MonsterStoneResult, int>? MonsterStoneEvent;

    public int NestStoneItemId { get; private set; }
    public bool NestCompleted { get; private set; }
    private ulong _nestEndsAtMs;

    public int NestSecondsLeft
    {
        get
        {
            ulong now = Time.GetTicksMsec();
            return _nestEndsAtMs > now ? (int)((_nestEndsAtMs - now + 999) / 1000) : 0;
        }
    }

    public void ClearNestTimer()
    {
        _nestEndsAtMs = 0;
        NestCompleted = false;
    }

    public void SendMonsterStone(int itemId)
    {
        var p = new Packet(GameOpcodes.GS_EVENT);
        p.WriteByte(MonsterStoneSub);
        p.WriteInt(itemId);
        _conn.Send(p);
    }

    private void HandleMonsterStone(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var result = (MonsterStoneResult)p.ReadByte();
        int itemId = result == MonsterStoneResult.Entered && p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        if (result == MonsterStoneResult.Entered)
        {
            NestStoneItemId = itemId;
            ClearNestTimer();
        }
        MonsterStoneEvent?.Invoke(result, itemId);
    }

    private void HandleNestTimer(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        int seconds = p.ReadShort();
        if (seconds < 0) return;
        NestCompleted = false;
        StartNestCountdown((uint)seconds);
    }

    private void HandleTempleEventFinish(Packet p)
    {
        if (p.RemainingBytes < 7) return;
        ushort eventId = p.ReadUShort();
        byte winner = p.ReadByte();
        uint seconds = p.ReadUInt();
        if (eventId != NestFinishEvent)
        {
            BorderWar.Finish(winner, seconds, DateTime.UtcNow);
            TempleEventFinishEvent?.Invoke(eventId, winner, seconds);
            return;
        }
        NestCompleted = true;
        StartNestCountdown(seconds);
    }

    private void StartNestCountdown(uint seconds) =>
        _nestEndsAtMs = Time.GetTicksMsec() + seconds * 1000UL;
}
