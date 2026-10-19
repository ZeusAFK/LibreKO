namespace LibreKO.Network;

public readonly struct EquipmentView
{
    public readonly string Name;
    public readonly int Class, Race, Face, Hair, Level, RebirthLevel, Nation;
    public readonly int MaxHp, MaxMp;
    public readonly int Str, StrBonus, Sta, StaBonus, Dex, DexBonus;
    public readonly int Intel, IntelBonus, Magic, MagicBonus;
    public readonly int Attack, Defence;
    public readonly int FireR, IceR, LightningR, MagicR, CurseR, PoisonR;
    public readonly List<(int Slot, int ItemId, short Durability, byte Flag)> Worn;

    public EquipmentView(string name, int cls, int race, int face, int hair,
        int level, int rebirthLevel, int nation, int maxHp, int maxMp,
        int str, int strBonus, int sta, int staBonus, int dex, int dexBonus,
        int intel, int intelBonus, int magic, int magicBonus,
        int attack, int defence,
        int fireR, int iceR, int lightningR, int magicR, int curseR, int poisonR,
        List<(int Slot, int ItemId, short Durability, byte Flag)> worn)
    {
        Name = name; Class = cls; Race = race; Face = face; Hair = hair;
        Level = level; RebirthLevel = rebirthLevel; Nation = nation;
        MaxHp = maxHp; MaxMp = maxMp;
        Str = str; StrBonus = strBonus; Sta = sta; StaBonus = staBonus;
        Dex = dex; DexBonus = dexBonus; Intel = intel; IntelBonus = intelBonus;
        Magic = magic; MagicBonus = magicBonus;
        Attack = attack; Defence = defence;
        FireR = fireR; IceR = iceR; LightningR = lightningR;
        MagicR = magicR; CurseR = curseR; PoisonR = poisonR;
        Worn = worn;
    }
}
