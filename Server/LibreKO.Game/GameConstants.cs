namespace LibreKO.Game;

public static class GameConstants
{
    public const int DailyOperationWindowMinutes = 1440;
    public const int InstanceRoomMinutes = 30;
    public const int CombatStateSeconds = 10;

    public static readonly int MaxAccountCharacters = 4;

    public const float MaxNpcInteractionRangeSq = 121.0f;

    public static class ItemSlots
    {
        public static readonly byte RightEar = 0;
        public static readonly byte Head = 1;
        public static readonly byte LeftEar = 2;
        public static readonly byte Neck = 3;
        public static readonly byte Breast = 4;
        public static readonly byte Pet = 5;
        public static readonly byte RightHand = 6;
        public static readonly byte Waist = 7;
        public static readonly byte LeftHand = 8;
        public static readonly byte RightRing = 9;
        public static readonly byte Leg = 10;
        public static readonly byte LeftRing = 11;
        public static readonly byte Glove = 12;
        public static readonly byte Foot = 13;
    }
}
