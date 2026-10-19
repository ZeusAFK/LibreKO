namespace LibreKO.Game.Protocol;

public enum WarehouseSubOpcode : byte
{
    Open = 1,
    Input = 2,
    Output = 3,
    Move = 4,
    InventoryMove = 5,
    Refresh = 6,
    ItemLost = 7,
    Request = 16,
}

public enum ClanWarehouseResult : byte
{
    Failed = 0,
    Succeeded = 1,
    NotInClan = 2,
    NoRights = 3,
    NotStorable = 4,
}

public enum VipWarehouseResult : byte
{
    Failed = 0,
    Succeeded = 1,
    Rejected = 2,
    Expired = 3,
    NotAVaultKey = 4,
    RentalAtCap = 5,
}

public enum VipWarehouseSubOpcode : byte
{
    Open = 1,
    Input = 2,
    Output = 3,
    Store = 4,
    InventoryMove = 5,
    UseVault = 6,
    KeyExpired = 7,
    SetPassword = 8,
    CancelPassword = 9,
    ChangePassword = 10,
    EnterPassword = 11,
}
