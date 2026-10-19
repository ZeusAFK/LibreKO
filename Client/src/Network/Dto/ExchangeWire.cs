namespace LibreKO.Network;

public enum ExchangeSubOpcode : byte
{
    Request = 1,
    Agree = 2,
    Add = 3,
    OtherAdd = 4,
    Decide = 5,
    OtherDecide = 6,
    Done = 7,
    Cancel = 8,
    Duplicate = 9,
}
