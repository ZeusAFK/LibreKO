using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Persistence.Seed.Entities;
using Xunit;

namespace LibreKO.Game.Tests;

public class ItemTableSeedTests
{
    private const int WoodStaff = 180110000;
    private const int Dagger = 110110000;
    private const int SelfNamedKhanjar = 1110429500;
    private const int FineYard = 110151000;

    private static readonly Lazy<ItemCatalog> Catalog = new(() =>
        new ItemCatalog(new ItemBaseSeed().GetSeedData(), new ItemVariantSeed().GetSeedData()));

    [Fact]
    public void TheWoodStaffComesInEveryUpgradeLevel()
    {
        for (var level = 1; level <= 10; level++)
            Catalog.Value[WoodStaff + level].Name.Should().Be($"Wood Staff(+{level})");
    }

    [Fact]
    public void AnUpgradeCarriesItsBonusOnTopOfTheBase()
    {
        var dagger = Catalog.Value[Dagger];
        var plusFive = Catalog.Value[Dagger + 5];

        plusFive.Damage.Should().BeGreaterThan(dagger.Damage);
        plusFive.BuyPrice.Should().BeGreaterThan(dagger.BuyPrice);
        plusFive.Kind.Should().Be(dagger.Kind);
    }

    [Fact]
    public void ASelfNamedWeaponResolvesThroughItsBlock()
    {
        Catalog.Value[SelfNamedKhanjar].Name.Should().Be("<selfname>'s Khanjar");
    }

    [Fact]
    public void AnItemTheClientDoesNotKnowIsNoItem()
    {
        Catalog.Value.ContainsKey(FineYard).Should().BeFalse();
    }

    [Theory]
    [InlineData(379042000, "Fang of Bakirra")]
    [InlineData(379046000, "Feather of Harpy Queen")]
    [InlineData(379047000, "Holy Water of Temple")]
    public void TheMasterSkillMaterialsAreItems(int itemId, string name)
    {
        Catalog.Value[itemId].Name.Should().Be(name);
    }

    [Fact]
    public void EveryVariantIsKeyedOnce()
    {
        new ItemVariantSeed().GetSeedData().Select(variant => (variant.VariantSet, variant.Id)).Should().OnlyHaveUniqueItems();
    }
}
