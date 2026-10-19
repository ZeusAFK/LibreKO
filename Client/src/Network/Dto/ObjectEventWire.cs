namespace LibreKO.Network;

public enum ObjectEventType : byte
{
    Bind = 0,
    Gate = 1,
    Gate2 = 2,
    GateLever = 3,
    FlagLever = 4,
    WarpGate = 5,
    RemoveBind = 7,
    Anvil = 8,
    Turn = 10,
    Effect = 11,
    KeyGate = 12,
    PoisonGas = 13,
    LeverFlip = 14,
}

public static class ObjectEventWire
{
    private const int GateStateBytes = 5;

    public static bool IsGateObject(ObjectEventType type) =>
            type is ObjectEventType.Gate or ObjectEventType.Gate2 or ObjectEventType.GateLever or ObjectEventType.FlagLever
                or ObjectEventType.KeyGate or ObjectEventType.LeverFlip;

    public static (int Id, bool On)? ReadShapeState(Packet p) =>
            p.RemainingBytes < GateStateBytes ? null : (p.ReadInt(), p.ReadByte() != 0);
}
