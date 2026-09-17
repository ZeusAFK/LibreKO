using System;

namespace LibreKO.Network;

public partial class Net
{
    private const byte BattleEventOpenSub   = 1;
    private const byte BattleEventResultSub = 3;

    public const byte BattleZoneOpen      = 0x00;
    public const byte BattleZoneClose     = 0x01;
    public const byte BattleZoneSnowOpen  = 0x09;

    public const byte BattleDeclareWinner = 0x02;
    public const byte BattleDeclareLoser  = 0x03;

    public const byte BattleStateNone   = 0;
    public const byte BattleStateNation = 1;
    public const byte BattleStateSnow   = 2;

    public event Action<int, int>? BattleZoneToggleEvent;

    public event Action<int, int, int>? BattleStatusEvent;

    public event Action<int, int>? BattleResultEvent;

    public event Action<int, int, int>? BattleScoreEvent;

    private void HandleBattleEvent(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case BattleEventOpenSub:
            {
                if (p.RemainingBytes < 2) return;
                int a = p.ReadByte();
                int zone = p.ReadByte();
                if (p.RemainingBytes >= 4)
                {
                    int remaining = p.ReadInt();
                    BattleStatusEvent?.Invoke(a, zone, remaining);
                }
                else
                {
                    BattleZoneToggleEvent?.Invoke(a, zone);
                }
                break;
            }
            case BattleEventResultSub:
            {
                if (p.RemainingBytes < 2) return;
                int declareType = p.ReadByte();
                int nation = p.ReadByte();
                BattleResultEvent?.Invoke(declareType, nation);
                break;
            }
        }
    }

    private void HandleMapEvent(Packet p)
    {
        if (p.RemainingBytes < 5) return;
        int eventType = p.ReadByte();
        int karus = p.ReadShort();
        int elmo = p.ReadShort();
        BattleScoreEvent?.Invoke(eventType, karus, elmo);
    }

    public void SendBattleStatusRequest()
    {
        var p = new Packet(GameOpcodes.GS_BATTLE_EVENT);
        p.WriteByte(BattleEventOpenSub);
        _conn.Send(p);
    }

    public void SendMapEvent(byte eventType = 0)
    {
        var p = new Packet(GameOpcodes.GS_MAP_EVENT);
        p.WriteByte(eventType);
        _conn.Send(p);
    }
}
