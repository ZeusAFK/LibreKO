using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public readonly record struct WeaponResistances(
    short Dagger,
    short Jamadar,
    short Sword,
    short Axe,
    short Mace,
    short Spear,
    short Bow)
{
    private const int StandardDivisor = 200;
    private const int JamadarDivisor = 150;

    public static WeaponResistances Of(DerivedStats stats) => new(
        stats.DaggerR, stats.JamadarR, stats.SwordR,
        stats.AxeR, stats.MaceR, stats.SpearR, stats.BowR);

    public int Reduction(int damage, ItemKind weapon) => weapon switch
    {
        ItemKind.Dagger => damage * Dagger / StandardDivisor,
        ItemKind.Jamadhar => damage * Jamadar / JamadarDivisor,
        ItemKind.SwordOneHand or ItemKind.SwordTwoHand => damage * Sword / StandardDivisor,
        ItemKind.AxeOneHand or ItemKind.AxeTwoHand => damage * Axe / StandardDivisor,
        ItemKind.ClubOneHand or ItemKind.ClubTwoHand or ItemKind.Mace
            => damage * Mace / StandardDivisor,
        ItemKind.SpearOneHand or ItemKind.SpearTwoHand => damage * Spear / StandardDivisor,
        ItemKind.Bow or ItemKind.Crossbow or ItemKind.LongBow or ItemKind.Launcher
            => damage * Bow / StandardDivisor,
        _ => 0,
    };
}
