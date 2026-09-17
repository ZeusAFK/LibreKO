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
    Close = 16,
}

public enum ClanWarehouseResult : byte
{
    Failed = 0,
    Succeeded = 1,
}

public enum VipWarehouseResult : byte
{
    Failed = 0,
    Succeeded = 1,
    Rejected = 2,
    Expired = 3,
}

public enum VipWarehouseSubOpcode : byte
{
    Open = 1,
    Input = 2,
    Output = 3,
    Store = 4,
    InventoryMove = 5,
    UseVault = 6,
    SetPassword = 8,
    CancelPassword = 9,
    ChangePassword = 10,
    EnterPassword = 11,
}
