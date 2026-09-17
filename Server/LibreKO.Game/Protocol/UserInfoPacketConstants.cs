namespace LibreKO.Game.Protocol;

public enum PlayerInspectSubOpcode : byte
{
    Nearby = 1,
    Detail = 2,
    NearbyAll = 3,
    Announce = 4,
    DetailById = 5,
    Equipment = 6,
}

public enum EquipmentViewResult : short
{
    Accepted = 0,
    NoSuchUser = -1,
    CannotChooseYourself = -2,
    NotInSameRegion = -3,
    NoViewEquipmentItem = -4,
}

internal static class UserInfoPacketConstants
{
    public const int NameMax = 20;
    public const float MaxInspectDistance = 60f;
}
