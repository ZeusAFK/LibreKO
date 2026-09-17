namespace LibreKO.Domain;

public enum ItemFlag : byte
{
    Unsealed = 0,
    Rented = 1,
    CharacterSeal = 2,
    Duplicate = 3,
    Sealed = 4,
    NotBound = 7,
    Bound = 8,
}
