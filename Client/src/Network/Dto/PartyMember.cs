namespace LibreKO.Network;

public readonly struct PartyMember
{
    public readonly int CharId;
    public readonly byte SuccessCode;
    public readonly string Name;
    public readonly int MaxHp, Hp;
    public readonly int Level;
    public readonly int Class;
    public readonly int MaxMp, Mp;

    public PartyMember(int charId, byte successCode, string name,
        int maxHp, int hp, int level, int cls, int maxMp, int mp)
    {
        CharId = charId; SuccessCode = successCode; Name = name;
        MaxHp = maxHp; Hp = hp; Level = level; Class = cls; MaxMp = maxMp; Mp = mp;
    }

    public bool BecameLeader => SuccessCode == 100;
}
