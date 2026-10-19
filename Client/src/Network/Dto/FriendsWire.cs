namespace LibreKO.Network;

public enum FriendAddResult : byte
{
    Succeeded = 0,
    Failed = 1,
    ListFull = 2,
}

public enum FriendRemoveResult : byte
{
    Succeeded = 0,
    Failed = 1,
    NotOnTheList = 2,
}

public enum FriendRequestResult : byte
{
    ListFull = 1,
    AlreadyRegistered = 2,
    InProgress = 3,
    Expired = 4,
    CannotAdd = 5,
    NationMismatch = 6,
    TargetCannotAdd = 7,
    Denied = 8,
}

public enum FriendSubOpcode : byte
{
    StatusList = 1,
    Add = 3,
    Remove = 4,
    LoggedIn = 5,
    Details = 6,
    Request = 16,
    RequestResult = 17,
}

public static class FriendsWire
{
    public const int MaxNameLength = 20;
}
