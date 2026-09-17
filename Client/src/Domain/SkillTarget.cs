namespace LibreKO.Domain;

public static class SkillTarget
{
    public const int None = 0;

    public const int Self = 1;
    public const int FriendWithMe = 2;
    public const int FriendOnly = 3;
    public const int Party = 4;
    public const int NpcOnly = 5;
    public const int PartyAll = 6;
    public const int EnemyOnly = 7;
    public const int All = 8;
    public const int AreaEnemy = 10;
    public const int AreaFriend = 11;
    public const int AreaAll = 12;
    public const int Area = 13;
    public const int DeadFriendOnly = 25;

    public static bool IsHostile(int moral) =>
        moral is None or NpcOnly or EnemyOnly or All or AreaEnemy or Area;

    public static bool IsFriendly(int moral) =>
        moral is FriendWithMe or FriendOnly or Party;

    public static bool IsDeadFriend(int moral) => moral is DeadFriendOnly;

    public static bool IsGroundArea(int moral) => moral is AreaEnemy or AreaFriend or AreaAll;

    public static bool IsCasterArea(int moral) => moral is PartyAll or Area;
}
