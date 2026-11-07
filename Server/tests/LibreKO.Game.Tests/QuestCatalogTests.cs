using FluentAssertions;
using LibreKO.Quests.Catalog;

namespace LibreKO.Game.Tests;

public class QuestCatalogTests
{
    [Fact]
    public void ItemsAreBuiltFromTheBasesAndTheirVariants()
    {
        var directory = Path.Combine(Path.GetTempPath(), "quest-catalog-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "ItemBases.json"),
                """[{"Id":810123000,"Name":"Apple","VariantSet":0,"Countable":1},{"Id":110110000,"Name":"Dagger","VariantSet":0,"Countable":0}]""");
            File.WriteAllText(Path.Combine(directory, "ItemVariants.set00.json"),
                """[{"VariantSet":0,"Id":3,"Name":"Upgrade","MadeFor":0,"Rarity":5},{"VariantSet":0,"Id":405,"Name":"Grove Blade","MadeFor":120110000,"Rarity":4}]""");
            var catalog = JsonQuestCatalog.Load(directory);
            catalog.KnowsItems.Should().BeTrue();
            catalog.ItemName(810123000).Should().Be("Apple");
            catalog.ItemStacks(810123000).Should().BeTrue();
            catalog.ItemName(110110003).Should().Be("Dagger(+3)");
            catalog.ItemStacks(110110003).Should().BeFalse();
            catalog.ItemName(110110405).Should().BeNull();
            catalog.ItemName(110110004).Should().BeNull();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
