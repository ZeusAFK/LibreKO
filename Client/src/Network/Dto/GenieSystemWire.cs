namespace LibreKO.Network;

public enum GenieSystemSubOpcode : byte
{
    InfoRequest = 1,
    UpdateRequest = 2,
}

public enum GenieSystemCommand : byte
{
    UseSpiritPotion = 1,
    LoadOptions = 2,
    SaveOptions = 3,
    Start = 4,
    Stop = 5,
    RemainingTime = 6,
    Activated = 7,
    UseHammer = 8,
}

public enum GenieSystemAction : byte
{
    Move = 1,
    Rotate = 2,
    MainAttack = 3,
    Magic = 4,
}

public enum GenieSystemResult : ushort
{
    Acknowledged = 1,
}

public enum GenieState : byte
{
    Inactive = 0,
    Active = 1,
}

public static class GenieSystemWire
{
    public const int OptionBytes = Domain.GenieOptionsBlob.Size;
}
