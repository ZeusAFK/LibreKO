namespace LibreKO.Network;

public enum TargetStateSubOpcode : byte
{
    DropList = 1,
    Effects = 2,
    Control = 3,
}

public static class ControlFlags
{
    public const byte Stunned = 1;
    public const byte Frozen = 2;
    public const byte Blinded = 4;
    public const byte Slowed = 8;
    public const byte AttackSlowed = 16;
}
