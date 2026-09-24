namespace LibreKO.Common.Domain.Services;

public sealed record StatBonus(short Strength, short Stamina, short Dexterity, short Intelligence, short Magic)
{
    public static readonly StatBonus None = new(0, 0, 0, 0, 0);
}
