using System;

namespace LibreKO.Network;

public partial class Net
{
    public const byte ObjectEventBind = 0;
    public const byte ObjectEventGate = 1;
    public const byte ObjectEventGateLever = 2;
    public const byte ObjectEventFlagLever = 3;
    public const byte ObjectEventWarpGate = 5;
    public const byte ObjectEventRemoveBind = 7;
    public const byte ObjectEventAnvil = 8;

    public event Action<byte, bool, int>? ObjectEventResultEvent;

    public event Action<int, int, byte, int, int, bool>? ObjectEventGateStateEvent;

    private void HandleObjectEvent(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        byte type = p.ReadByte();
        bool success = p.ReadByte() != 0;
        int objectId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        ObjectEventResultEvent?.Invoke(type, success, objectId);
    }

    public void ParseGateStatePush(Packet p)
    {
        if (p.RemainingBytes < 14) return;
        int uniqueId = p.ReadInt();
        int npcId = p.ReadShort();
        byte npcType = p.ReadByte();
        int maxHp = p.ReadInt();
        int hp = p.ReadInt();
        bool gateOpen = p.ReadByte() != 0;
        ObjectEventGateStateEvent?.Invoke(uniqueId, npcId, npcType, maxHp, hp, gateOpen);
    }

    public void SendObjectEvent(short objectIndex, int npcId)
    {
        var p = new Packet(GameOpcodes.GS_OBJECT_EVENT);
        p.WriteShort(objectIndex);
        p.WriteInt(npcId);
        _conn.Send(p);
    }
}
