namespace LibreKO.Common.Enums;

public enum ItemSealResult : byte
{
    Succeeded = 1,
    Failed = 2,
    NeedCoins = 3,
    WrongCode = 4,
    MissingMaterial = 5,
    NoCodeSet = 20,
}
