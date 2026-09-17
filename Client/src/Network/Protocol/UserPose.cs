namespace LibreKO.Network;

public static class UserPose
{
    public const byte Standing = 1;
    public const byte Sitting = 2;
    public const byte Dead = 3;
    public const byte Asleep = 4;
    public const byte Awake = 5;
    public const byte Mining = 7;
    public const byte Fishing = 8;
}

public static class StateChange
{
    public const byte Pose = 1;
    public const byte NeedParty = 2;
    public const byte Abnormal = 3;
    public const byte Action = 4;
    public const byte Visibility = 5;
    public const byte Stealth = 7;
    public const byte CombatStance = 8;
    public const byte Transformation = 19;

    public const byte StanceRelaxed = 0;
    public const byte StanceReady = 1;
}
