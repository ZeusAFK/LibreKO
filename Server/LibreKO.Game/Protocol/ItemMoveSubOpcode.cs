namespace LibreKO.Game.Protocol;

public enum ItemMoveSubOpcode : byte
{
    Failed = 0,
    Move = 1,
    ArrangeInventory = 2,
}
