using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Game.World;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class DropSummaryTests
{
    private const short DropGroup = 501;
    private const int Sword = 110_110_001;
    private const int Potion = 389_010_000;
    private const int GroupId = 1_200;
    private const int GroupA = 120_110_001;
    private const int GroupB = 120_110_002;

    [Fact]
    public void ItemsComeWithABandInsteadOfARateAndGroupsOpenUp()
    {
        var data = Substitute.For<IGameDataService>();
        data.GetNpcItem(DropGroup, true).Returns(new NpcItemData
        {
            Index = DropGroup, IsMonster = true,
            Item1 = Potion, Percent1 = 6000,
            Item2 = Sword, Percent2 = 50,
            Item3 = GroupId, Percent3 = 3000,
        });
        data.MakeItemGroupTable.Returns(new Dictionary<int, int[]> { [GroupId] = [GroupA, GroupB] });
        data.GetItem(Arg.Any<int>()).Returns(call => new ItemData { Num = call.Arg<int>() });
        var npc = new NpcInstance { IsMonster = true, DropItemGroup = DropGroup, GoldDrop = 1000, Experience = 420 };

        var summary = DropSummaries.For(npc, data);

        summary.CoinMin.Should().Be(700);
        summary.CoinMax.Should().Be(1000);
        summary.Experience.Should().Be(420);
        summary.Items.Should().Equal(
            new DropLine(Potion, DropBand.Common),
            new DropLine(GroupA, DropBand.Uncommon),
            new DropLine(GroupB, DropBand.Uncommon),
            new DropLine(Sword, DropBand.VeryRare));
        summary.Total.Should().Be(4);
    }

    [Fact]
    public void AListCutAtTheLineLimitStillCountsEveryItem()
    {
        const int BigGroup = 1_300;
        const int GroupSize = DropSummaries.MaxLines + 6;
        var data = Substitute.For<IGameDataService>();
        data.GetNpcItem(DropGroup, true).Returns(new NpcItemData
        {
            Index = DropGroup, IsMonster = true,
            Item1 = BigGroup, Percent1 = 5000,
        });
        data.MakeItemGroupTable.Returns(new Dictionary<int, int[]>
        {
            [BigGroup] = Enumerable.Range(0, GroupSize).Select(i => GroupA + i).ToArray(),
        });
        data.GetItem(Arg.Any<int>()).Returns(call => new ItemData { Num = call.Arg<int>() });
        var npc = new NpcInstance { IsMonster = true, DropItemGroup = DropGroup };

        var summary = DropSummaries.For(npc, data);

        summary.Items.Should().HaveCount(DropSummaries.MaxLines);
        summary.Total.Should().Be(GroupSize);
    }
}
