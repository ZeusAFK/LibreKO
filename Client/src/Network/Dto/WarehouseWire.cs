namespace LibreKO.Network;

public enum WarehouseSubOpcode : byte
{
    Open = 1,
    Input = 2,
    Output = 3,
    Move = 4,
    InventoryMove = 5,
    Request = 16,
}

public enum WarehouseOpenResult : byte
{
    Opened = 1,
    NotEnoughMoney = 2,
}

public static class WarehouseWire
{
    public const int Slots = 192;
}
