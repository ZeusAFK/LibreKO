namespace LibreKO.Domain;

public readonly record struct PetItemInfo(int Index, string Name, int Attack, int Level, int ExpPercent, int Satisfaction, int Tail = 0);

public sealed class PetSheet
{
    public const int ExpPercentScale = 10_000;
    public const int MaxSatisfaction = 10_000;
    public const int InventorySize = 4;
    public const int ResistanceCount = 6;
    public const int NameMaxLength = 15;

    public int Index;
    public string Name = "";
    public int Class;
    public int Level;
    public int ExpPercent;
    public int MaxHp, Hp, MaxMp, Mp;
    public int Satisfaction;
    public int Attack, Defence;
    public PetMode Mode = PetMode.Defence;
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
