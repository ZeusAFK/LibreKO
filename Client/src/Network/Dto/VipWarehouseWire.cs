namespace LibreKO.Network;

public enum VipWarehouseSubOpcode : byte
{
    Open = 0x01,
    Input = 0x02,
    Output = 0x03,
    Store = 0x04,
    UseVault = 0x06,
    KeyExpired = 0x07,
    SetPassword = 0x08,
    CancelPassword = 0x09,
    ChangePassword = 0x0A,
    EnterPassword = 0x0B,
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

public static class VipWarehouseWire
{
    public const int Slots = 48;
}
