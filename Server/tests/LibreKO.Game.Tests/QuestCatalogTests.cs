using FluentAssertions;
using LibreKO.Quests.Catalog;

namespace LibreKO.Game.Tests;

public class QuestCatalogTests
{
    [Fact]
    public void ItemShardsAreLoadedAndMonolithicSeedTakesPrecedence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "quest-catalog-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Items.slot00.json"),
                """[{"Num":123,"Name":"Apple","Countable":1}]""");
            File.WriteAllText(Path.Combine(directory, "Items.slot01.json"),
                """[{"Num":456,"Name":"Sword","Countable":0}]""");
            var catalog = JsonQuestCatalog.Load(directory);
            catalog.KnowsItems.Should().BeTrue();
            catalog.ItemName(123).Should().Be("Apple");
            catalog.ItemStacks(123).Should().BeTrue();
            catalog.ItemName(456).Should().Be("Sword");
            catalog.ItemStacks(456).Should().BeFalse();
            File.WriteAllText(Path.Combine(directory, "Items.json"),
                """[{"Num":789,"Name":"Shield","Countable":0}]""");
            catalog = JsonQuestCatalog.Load(directory);
            catalog.ItemName(123).Should().BeNull();
            catalog.ItemName(789).Should().Be("Shield");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
