namespace LibreKO.Domain;

public enum ItemSealResult : byte
{
    Succeeded = 1,
    Failed = 2,
    NeedCoins = 3,
    WrongCode = 4,
    MissingMaterial = 5,
    NotSealable = 6,
    TooSoon = 7,
    NoCodeSet = 20,
    CodeLockedOut = 21,
    WrongVaultPassword = 22,
}
