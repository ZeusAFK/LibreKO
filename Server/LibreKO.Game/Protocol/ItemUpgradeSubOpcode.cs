namespace LibreKO.Game.Protocol;

public enum ItemUpgradeSubOpcode : byte
{
    AnvilOpen = 1,
    Upgrade = 2,
    UpgradeAccessories = 3,
    BifrostRequest = 4,
    BifrostExchange = 5,
    UpgradeRebirth = 7,
    ItemSeal = 8,
}
