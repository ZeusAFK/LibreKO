using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Common.Tests;

public class ItemCatalogTests
{
    private const byte StaffSet = 10;
    private const byte KhanjarSet = 0;
    private const int WoodStaff = 180110000;
    private const int OakStaff = 180120000;
    private const int Khanjar = 1110429000;
    private const int UpgradeThree = 3;
    private const int UpgradeTen = 10;
    private const int StaffOnlyVariant = 405;
    private const int SelfNamed = 1500;
    private const int ReverseThirteen = 851;
    private const short StaffDamage = 20;
    private const short UpgradeDamage = 8;
    private const int StaffPrice = 1000;
    private const short PriceMultiplier = 3;
    private const short StaffClass = 1;
    private const short ClassStep = 1;

    private static ItemBaseData Base(int id, string name, byte set) => new()
    {
        Id = id, Name = name, VariantSet = set, Damage = StaffDamage, BuyPrice = StaffPrice, ItemClass = StaffClass,
        Duration = 5000, UnbindStones = 2,
    };

    private static ItemVariantData Variant(byte set, int id, byte rarity = ItemRarity.Upgrade, string name = "Upgrade", int madeFor = 0) => new()
    {
        VariantSet = set, Id = id, Name = name, Rarity = rarity, MadeFor = madeFor, Damage = UpgradeDamage,
        PriceMultiplier = PriceMultiplier, GradeClass = ClassStep, Duration = 500, UnbindStones = 3, Str = 4,
    };

    private static ItemCatalog Catalog() => new(
        [Base(WoodStaff, "Wood Staff", StaffSet), Base(OakStaff, "Oak Staff", StaffSet), Base(Khanjar, "<selfname>'s Khanjar", KhanjarSet)],
        [
            Variant(StaffSet, UpgradeThree),
            Variant(StaffSet, UpgradeTen),
            Variant(StaffSet, StaffOnlyVariant, ItemRarity.Unique, "Wood Staff of the Grove", WoodStaff),
            Variant(StaffSet, ReverseThirteen, ItemRarity.Reverse, "Light"),
            Variant(KhanjarSet, SelfNamed, ItemRarity.Unique, "<selfname>'s Khanjar", Khanjar),
            Variant(KhanjarSet, SelfNamed % ItemCatalog.BaseSpan, ItemRarity.Unique, "Another blade"),
        ]);

    [Fact]
    public void ABaseIdIsTheBaseItemAlone()
    {
        Catalog().TryGetValue(WoodStaff, out var item).Should().BeTrue();

        item!.Name.Should().Be("Wood Staff");
        item.Damage.Should().Be(StaffDamage);
        item.BuyPrice.Should().Be(StaffPrice);
        item.Rarity.Should().Be(ItemRarity.Regular);
        item.GradeClass.Should().Be(StaffClass);
        item.StrB.Should().Be(0);
    }

    [Fact]
    public void AnUpgradeAddsItsVariantToTheBase()
    {
        var item = Catalog()[WoodStaff + UpgradeThree];

        item.Name.Should().Be("Wood Staff(+3)");
        item.Damage.Should().Be(StaffDamage + UpgradeDamage);
        item.BuyPrice.Should().Be(StaffPrice * PriceMultiplier);
        item.Duration.Should().Be(5500);
        item.UnbindStones.Should().Be(5);
        item.StrB.Should().Be(4);
        item.Rarity.Should().Be(ItemRarity.Upgrade);
        item.Grade.Should().Be(UpgradeThree);
        item.ItemClass.Should().Be(StaffClass);
        item.GradeClass.Should().Be(StaffClass + ClassStep);
    }

    [Fact]
    public void ATenthUpgradeIsNamedPlusTen()
    {
        Catalog()[WoodStaff + UpgradeTen].Name.Should().Be("Wood Staff(+10)");
    }

    [Fact]
    public void AReverseVariantTakesItsLevelFromThePlusColumn()
    {
        var catalog = new ItemCatalog(
            [Base(WoodStaff, "Wood Staff", StaffSet)],
            [new ItemVariantData { VariantSet = StaffSet, Id = ReverseThirteen, Rarity = ItemRarity.Reverse, Name = "Light", Plus = 13 }]);

        catalog[WoodStaff + ReverseThirteen].Name.Should().Be("Wood Staff(+13)");
        catalog[WoodStaff + ReverseThirteen].UpgradeLevel.Should().Be(13);
    }

    [Fact]
    public void APlusTenItemHasUpgradeLevelTenThoughItsGradeIsTheLastDigitOfItsId()
    {
        var item = Catalog()[WoodStaff + UpgradeTen];

        item.UpgradeLevel.Should().Be(UpgradeTen);
        item.Grade.Should().Be(0);
    }

    [Fact]
    public void AUniqueVariantNamesTheItem()
    {
        Catalog()[WoodStaff + StaffOnlyVariant].Name.Should().Be("Wood Staff of the Grove");
    }

    [Fact]
    public void AVariantMadeForOneBaseIsNoItemOnAnotherBaseOfItsSet()
    {
        var catalog = Catalog();

        catalog.ContainsKey(WoodStaff + StaffOnlyVariant).Should().BeTrue();
        catalog.ContainsKey(OakStaff + StaffOnlyVariant).Should().BeFalse();
        catalog.ContainsKey(OakStaff + UpgradeThree).Should().BeTrue();
    }

    [Fact]
    public void AnIdWithoutABaseOrAVariantIsNoItem()
    {
        var catalog = Catalog();

        catalog.ContainsKey(WoodStaff + 7).Should().BeFalse();
        catalog.ContainsKey(180130000).Should().BeFalse();
        catalog.TryGetValue(0, out _).Should().BeFalse();
    }

    [Fact]
    public void TheBillionsDigitPicksTheVariantBlock()
    {
        var item = Catalog()[Khanjar + SelfNamed % ItemCatalog.BaseSpan];

        item.Name.Should().Be("<selfname>'s Khanjar");
        ItemCatalog.VariantIdOf(Khanjar + SelfNamed % ItemCatalog.BaseSpan).Should().Be(SelfNamed);
    }

    [Fact]
    public void TheCatalogListsEveryBaseAndEveryVariantThatFitsIt()
    {
        var catalog = Catalog();

        catalog.Keys.Should().BeEquivalentTo(new[]
        {
            WoodStaff, WoodStaff + UpgradeThree, WoodStaff + UpgradeTen, WoodStaff + StaffOnlyVariant, WoodStaff + ReverseThirteen,
            OakStaff, OakStaff + UpgradeThree, OakStaff + UpgradeTen, OakStaff + ReverseThirteen,
            Khanjar, Khanjar + SelfNamed % ItemCatalog.BaseSpan,
        });
        catalog.Count.Should().Be(catalog.Keys.Count());
        catalog.Values.Select(item => item.Num).Should().BeEquivalentTo(catalog.Keys);
    }

    [Fact]
    public void LargeSumsStayInRange()
    {
        var catalog = new ItemCatalog(
            [new ItemBaseData { Id = WoodStaff, Name = "Wood Staff", VariantSet = StaffSet, Duration = 20000, BuyPrice = 1_000_000_000 }],
            [new ItemVariantData { VariantSet = StaffSet, Id = UpgradeThree, Rarity = ItemRarity.Upgrade, Duration = 19500, PriceMultiplier = 4 }]);

        var item = catalog[WoodStaff + UpgradeThree];

        item.Duration.Should().Be(short.MaxValue);
        item.BuyPrice.Should().Be(int.MaxValue);
    }

    [Fact]
    public void TheSameItemIsReturnedForRepeatedLookups()
    {
        var catalog = Catalog();

        catalog[WoodStaff + UpgradeThree].Should().BeSameAs(catalog[WoodStaff + UpgradeThree]);
    }
}
