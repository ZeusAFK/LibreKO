namespace LibreKO.Game.Protocol;

public enum PowerUpStoreResult : byte
{
    Failed = 0,
    Succeeded = 1,
    NotEnoughCash = 2,
    Unavailable = 3,
    RecipientNotFound = 4,
    RecipientIsSelf = 5,
    PriceChanged = 6,
    RecipientNotAllowed = 7,
}
