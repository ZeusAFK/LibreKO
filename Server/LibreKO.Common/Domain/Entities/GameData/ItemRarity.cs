namespace LibreKO.Common.Domain.Entities.GameData;

public static class ItemRarity
{
    public const byte Regular = 0;
    public const byte Magic = 1;
    public const byte Rare = 2;
    public const byte Craft = 3;
    public const byte Unique = 4;
    public const byte Upgrade = 5;
    public const byte Event = 6;
    public const byte Pet = 7;
    public const byte Cospre = 8;
    public const byte CospreTransparent = 9;
    public const byte Reverse = 11;
    public const byte ReverseUnique = 12;

    public static bool NamesTheItem(byte rarity) => rarity is Craft or Unique or Event or ReverseUnique;

    public static bool TakesUpgradeLevel(byte rarity) =>
        rarity is not (Unique or Event or ReverseUnique or Pet or Cospre or CospreTransparent);
}
