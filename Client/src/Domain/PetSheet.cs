namespace LibreKO.Domain;

public readonly record struct PetItemInfo(int Index, string Name, int Attack, int Level, int ExpPercent, int Satisfaction);

public sealed class PetSheet
{
    public const int ExpPercentScale = 10_000;
    public const int MaxSatisfaction = 10_000;
    public const int InventorySize = 4;
    public const int ResistanceCount = 6;
    public const int ModeSummoned = 1;
    public const int ModeDied = 2;
    public const int ModeAttack = 3;
    public const int ModeDefence = 4;
    public const int ModeLooting = 8;

    public int Index;
    public string Name = "";
    public int Class;
    public int Level;
    public int ExpPercent;
    public int MaxHp, Hp, MaxMp, Mp;
    public int Satisfaction;
    public int Attack, Defence;
    public int Mode = ModeDefence;
    public readonly int[] Resists = new int[ResistanceCount];
    public readonly ItemSlot[] Items = new ItemSlot[InventorySize];

    public float ExpFraction => ExpPercent / (float)ExpPercentScale;
    public float SatisfactionFraction => Satisfaction / (float)MaxSatisfaction;

    public bool PlaceConfirmed(int index, int position, ItemSlot item)
    {
        if (Index != index || position < 0 || position >= Items.Length) return false;
        Items[position] = item;
        return true;
    }
}
