namespace LibreKO.Network;

public enum GenderChangeSubOpcode : byte
{
    Change = 1,
}

public enum GenderChangeResult : byte
{
    Failed = 0,
    Changed = 1,
    NoItem = 2,
}
