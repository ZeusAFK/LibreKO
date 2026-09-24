using System;

namespace LibreKO.Network;

public partial class Net
{
    public const byte ObjectEventBind = 0;
    public const byte ObjectEventGate = 1;
    public const byte ObjectEventGate2 = 2;
    public const byte ObjectEventGateLever = 3;
    public const byte ObjectEventFlagLever = 4;
    public const byte ObjectEventKrowasGate = 12;
    public const byte ObjectEventWoo = 14;
    private const int GateStateBytes = 5;
    public const byte ObjectEventWarpGate = 5;
    public const byte ObjectEventRemoveBind = 7;
    public const byte ObjectEventAnvil = 8;

    public event Action<byte, bool, int>? ObjectEventResultEvent;

    public event Action<int, bool>? ObjectEventGateStateEvent;

    public static bool IsGateObject(byte type) =>
        type is ObjectEventGate or ObjectEventGate2 or ObjectEventGateLever or ObjectEventFlagLever
            or ObjectEventKrowasGate or ObjectEventWoo;

    private void HandleObjectEvent(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        byte type = p.ReadByte();
        bool success = p.ReadByte() != 0;
        if (success && IsGateObject(type) && p.RemainingBytes >= GateStateBytes)
        {
            int uniqueId = p.ReadInt();
            ObjectEventGateStateEvent?.Invoke(uniqueId, p.ReadByte() != 0);
            return;
        }
        int objectId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        ObjectEventResultEvent?.Invoke(type, success, objectId);
    }

    public void SendObjectEvent(short objectIndex, int npcId)
    {
        var p = new Packet(GameOpcodes.GS_OBJECT_EVENT);
        p.WriteShort(objectIndex);
        p.WriteInt(npcId);
        _conn.Send(p);
    }
}
