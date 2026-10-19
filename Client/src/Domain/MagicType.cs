namespace LibreKO.Domain;

public static class MagicType
{
    public const int None = 0;
    public const int Melee = 1;
    public const int Ranged = 2;
    public const int DotHeal = 3;
    public const int Buff = 4;
    public const int Special = 5;
    public const int Transform = 6;
    public const int Aoe = 7;
    public const int Warp = 8;
    public const int Stealth = 9;
}

public static class WarpType
{
    public const int Blink = 20;
    public const int Rush = 30;
    public const int Pull = 31;
}

public static class StealthKind
{
    public const int DispelOnMove = 1;
    public const int DispelOnAttack = 2;
    public const int SeeInvisible = 3;
    public const int SeeInvisibleParty = 4;
    public const int ChiefGuardian = 7;
    public const int PetSummon = 8;

    public static int ExpiryCode(int stateChange) => stateChange switch
    {
        DispelOnMove or DispelOnAttack => BuffExpiry.StealthEnded,
        SeeInvisible or SeeInvisibleParty => BuffExpiry.SightEnded,
        ChiefGuardian or PetSummon => BuffExpiry.SummonEnded,
        _ => 0,
    };
}

public static class MagicData
{
    public const int Length = 7;
}

public static class MagicSub
{
    public const int Casting = 1;
    public const int Flying = 2;
    public const int Effecting = 3;
    public const int Fail = 4;
    public const int DurationExpired = 5;
    public const int Cancel = 6;
    public const int CancelTransformation = 7;
    public const int ExtendDuration = 8;
    public const int TransformationList = 9;
    public const int TransformationRefused = 10;
}

public enum MagicFailText { None, CastingFailed, SkillFailed, Missed, NotEnoughMp }

public static class MagicFail
{
    public const int ResultSlot = 3;
    public const int QuietSlot = 4;
    public const short Quiet = 1;
    public const short Casting = -100;
    public const short ProjectileKilled = -101;
    public const short NoEffect = -103;
    public const short AttackZero = -104;
    public const short ProjectileKilledByType = -105;
    public const short NotEnoughMp = -106;

    public static short Code(short[] data) => data.Length > ResultSlot ? data[ResultSlot] : (short)0;

    public static bool IsMiss(short[] data) => Code(data) <= Casting;

    public static bool RefundsCooldown(short code, bool hasFlyingFx) =>
        code is not (ProjectileKilled or ProjectileKilledByType) && !hasFlyingFx;

    public static MagicFailText TextFor(short[] data) => Code(data) switch
    {
        Casting => data.Length > QuietSlot && data[QuietSlot] == Quiet ? MagicFailText.None : MagicFailText.CastingFailed,
        NoEffect => MagicFailText.SkillFailed,
        AttackZero => MagicFailText.Missed,
        NotEnoughMp => MagicFailText.NotEnoughMp,
        _ => MagicFailText.None,
    };
}

public static class HealTarget
{
    public const int Hp = 1;
    public const int Mp = 2;
    public const int HpPurchase = 20;
    public const int MpPurchase = 21;
}

public static class SpecialMagic
{
    public const int RemoveDot = 1;
    public const int RemoveBuff = 2;
    public const int Resurrect = 3;
    public const int ResurrectSelf = 4;
    public const int RemoveBless = 5;
    public const int LifeCrystal = 6;
}
