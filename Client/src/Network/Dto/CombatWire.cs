namespace LibreKO.Network;

public enum TargetHpEcho : byte
{
    None = 0,
    Poll = 1,
}

public enum AttackType : byte
{
    Melee = 1,
}

public enum RegeneSubOpcode : byte
{
    Town = 1,
    ResurrectChoice = 4,
    ResurrectAccept = 5,
}

public enum RespawnType : byte
{
    Normal = 1,
    ResurrectOffer = 4,
    Refused = 10,
}

public enum RespawnRefusal : ushort
{
    Standby = 4,
    MissingItem = 10,
}

public enum SkillBarSubOpcode : byte
{
    Save = 1,
    Load = 2,
}

public enum ResurrectionChoice : byte
{
    InPlace = 1,
    Town = 2,
}

public static class CombatWire
{
    public const int SkillBarMaxSlots = HotbarLayout.Total;
}

public readonly record struct AttackReply(int AttackType, int Result, int AttackerId, int TargetId, bool Chained);

public readonly record struct MagicReply(int Sub, int SkillId, int CasterId, int TargetId, short[] Data);
