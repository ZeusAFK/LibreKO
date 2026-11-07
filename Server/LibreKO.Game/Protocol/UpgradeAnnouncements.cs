using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.Protocol;

public static class UpgradeAnnouncements
{
    public const int WeaponArmourLevel = 8;
    public const int AccessoryLevel = 2;
    private const byte FirstAccessoryKind = 91;
    private const byte LastAccessoryKind = 94;

    public static bool Announced(ItemData item) =>
        item.Rarity is ItemRarity.Reverse or ItemRarity.ReverseUnique
        || item.UpgradeLevel >= (item.Kind is >= FirstAccessoryKind and <= LastAccessoryKind ? AccessoryLevel : WeaponArmourLevel);
}
