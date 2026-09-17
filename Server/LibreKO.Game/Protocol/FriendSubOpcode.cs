namespace LibreKO.Game.Protocol;

public enum FriendSubOpcode : byte
{
    StatusList = 1,
    Report = 2,
    Add = 3,
    Remove = 4,
    Details = 6,
}

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
