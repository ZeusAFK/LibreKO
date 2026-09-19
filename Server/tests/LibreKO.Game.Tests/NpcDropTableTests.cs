using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Persistence.Seed.Entities;
using LibreKO.Game.World;
using Xunit;

namespace LibreKO.Game.Tests;

public class NpcDropTableTests
{
    private const short SharedGroup = 100;
    private const short TreeGhostKingGroup = 5003;
    private const short LilithGroup = 2305;

    private static List<NpcItemData> Seed() => new NpcItemSeed().GetSeedData().ToList();

    [Fact]
    public void TheTwoSidesNoLongerShareAKey()
    {
        var rows = Seed();

        rows.Select(row => (row.Index, row.IsMonster)).Should().OnlyHaveUniqueItems(
            "the drop tables are keyed by index and by which prototype table the row came from");
    }

    [Fact]
    public void BothSidesOfASharedIndexSurviveSeeding()
    {
        var rows = Seed();

        var monster = rows.SingleOrDefault(row => row.Index == SharedGroup && row.IsMonster);
        var npc = rows.SingleOrDefault(row => row.Index == SharedGroup && !row.IsMonster);

        monster.Should().NotBeNull();
        npc.Should().NotBeNull();
        monster!.HasAnyDrop.Should().BeTrue("a monster group with loot must not be flattened by the empty row that shares its index");
        npc!.HasAnyDrop.Should().BeFalse();
    }

    [Theory]
    [InlineData(SharedGroup)]
    [InlineData(TreeGhostKingGroup)]
    [InlineData(LilithGroup)]
    public void GroupsThatLostTheirLootHaveItBack(short group)
    {
        var row = Seed().Single(candidate => candidate.Index == group && candidate.IsMonster);

        row.HasAnyDrop.Should().BeTrue();
        row.Item1.Should().BeGreaterThan(0);
        row.Percent1.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EverySeededMonsterGroupCarriesAllSevenSlots()
    {
        var rows = Seed();

        rows.Should().HaveCountGreaterThan(4000);
        rows.Count(row => row.HasAnyDrop).Should().BeGreaterThan(800);
        rows.Any(row => row.Item6 > 0).Should().BeTrue("slot six is populated in the source and used to be dropped on the floor");
    }

    [Fact]
    public void ADropRowOffersSevenSlots()
    {
        var row = new NpcItemData { Item7 = 42, Percent7 = 100 };

        row.GetDrops().Should().HaveCount(NpcItemData.DropSlots);
        row.GetDrops()[6].Should().Be((42, (short)100));
    }

    [Fact]
    public void AMonsterRemembersWhichPrototypeTableItCameFrom()
    {
        var monster = NpcInstance.FromData(
            new NpcData { Id = 1, IsMonster = true, Name = "Worm", ItemGroup = SharedGroup },
            new NpcPosData { Index = 1, ZoneId = 21, NpcId = 1, ActType = 1, NumNPC = 1 },
            1);

        var npc = NpcInstance.FromData(
            new NpcData { Id = 1, IsMonster = false, Name = "Guard", ItemGroup = SharedGroup },
            new NpcPosData { Index = 2, ZoneId = 21, NpcId = 1, ActType = 101, NumNPC = 1 },
            2);

        monster.IsMonster.Should().BeTrue();
        npc.IsMonster.Should().BeFalse();
        monster.DropItemGroup.Should().Be(npc.DropItemGroup, "they share an index; only the side tells them apart");
    }
}
