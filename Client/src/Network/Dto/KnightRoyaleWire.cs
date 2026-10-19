namespace LibreKO.Network;

public readonly record struct RoyaleSignUp(KnightRoyaleSignUpResult Result, int Participants, int Seconds, int Mode);

public readonly record struct RoyaleEntry(int Result, int Level, int MaxExp, int Exp);

public readonly record struct RoyaleStats(int CharId, int Level, int MaxExp, int Exp, int MaxHp, int Hp, int MaxMp, int Mp);

public readonly record struct RoyaleItem(int Slot, int ItemId, int Count, int Durability, int Flag);

public readonly record struct RoyaleLoot(int BoxId, int SlotOrCode, int ItemId, int Count);

public static class KnightRoyaleWire
{
    public const int LootFailed = 0xFFF9;
    public const int LootQuantity = 0xFFF5;
    public const int LootWeight = 0xFFFE;

    public static RoyaleSignUp ReadSignUp(Packet p)
    {
        var result = p.RemainingBytes >= 1 ? (KnightRoyaleSignUpResult)p.ReadByte() : KnightRoyaleSignUpResult.Count;
        int participants = 0, seconds = 0, mode = 0;
        if (result is KnightRoyaleSignUpResult.Count or KnightRoyaleSignUpResult.Open && p.RemainingBytes >= 1)
            participants = p.ReadByte();
        if (result == KnightRoyaleSignUpResult.Open && p.RemainingBytes >= 3)
        {
            seconds = p.ReadUShort();
            mode = p.ReadByte();
        }
        return new RoyaleSignUp(result, participants, seconds, mode);
    }

    public static RoyaleEntry ReadEntry(Packet p) => p.RemainingBytes >= 6
        ? new RoyaleEntry(p.ReadByte(), p.ReadByte(), p.ReadShort(), p.ReadShort())
        : default;

    public static RoyaleStats ReadStats(Packet p) => p.RemainingBytes >= 17
        ? new RoyaleStats(p.ReadInt(), p.ReadByte(), p.ReadShort(), p.ReadShort(), p.ReadShort(), p.ReadShort(), p.ReadShort(), p.ReadShort())
        : default;

    public static RoyaleItem ReadItem(Packet p) => p.RemainingBytes >= 10
        ? new RoyaleItem(p.ReadByte(), p.ReadInt(), p.ReadShort(), p.ReadShort(), p.ReadByte())
        : default;

    public static RoyaleLoot ReadLoot(Packet p)
    {
        if (p.RemainingBytes < 6) return default;
        int box = p.ReadInt();
        int slot = p.ReadUShort();
        if (slot is LootFailed or LootQuantity or LootWeight || p.RemainingBytes < 6)
            return new RoyaleLoot(box, slot, 0, 0);
        return new RoyaleLoot(box, slot, p.ReadInt(), p.ReadUShort());
    }

    public static (string Name, int Value) ReadNamed(Packet p)
    {
        string name = p.RemainingBytes >= 1 ? p.ReadSByteString() : "";
        int value = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        return (name, value);
    }

    public static (int X, int Z) ReadSpot(Packet p) => p.RemainingBytes >= 4 ? (p.ReadShort(), p.ReadShort()) : (0, 0);

    public static Domain.KnightRoyale.Reward ReadReward(Packet p) => p.RemainingBytes >= 5
        ? new Domain.KnightRoyale.Reward(p.ReadByte(), p.ReadByte(), p.ReadByte(), p.ReadShort())
        : default;
}

public enum KnightRoyaleSubOpcode : byte
{
    SignUp = 1,
    Entry = 2,
    Progress = 3,
    Notice = 4,
    Loot = 5,
    ItemAdd = 6,
    Restore = 7,
    Destroy = 8,
    Combine = 9,
}

public enum KnightRoyaleProgressKind : byte
{
    Exp = 1,
    Stats = 2,
}

public enum KnightRoyaleNoticeKind : byte
{
    Abandoned = 1,
    Gas = 2,
    Chest = 3,
    Reward = 4,
    Winner = 5,
    Delegate = 6,
    Survivors = 7,
}

public enum KnightRoyaleSignUpAction : byte
{
    Apply = 1,
    Cancel = 2,
}

public enum KnightRoyaleSignUpResult : byte
{
    Count = 0,
    Open = 1,
    ApplyBack = 2,
    Already = 3,
    Rejected = 4,
    Level = 5,
    DuplicateIp = 6,
}
