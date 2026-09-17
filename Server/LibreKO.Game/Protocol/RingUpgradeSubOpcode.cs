namespace LibreKO.Game.Protocol;

public enum RingUpgradeSubOpcode : byte
{
    Status = 1,
    Upgrade = 2,
}

public enum RingUpgradeResult : byte
{
    Failed = 0,
    Succeeded = 1,
    NotUpgradeable = 2,
}
