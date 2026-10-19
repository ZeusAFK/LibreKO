using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestRewardSlotTests
{
    private const int Filler = 700_000_000;
    private const int Reward = 123;
    private const int BackHalfSlot = 20;
    private const int NoFreeSlot = -1;

    private readonly IGameDataService data = Substitute.For<IGameDataService>();
    private readonly UserSession session;
    private readonly QuestScriptContext context;

    public QuestRewardSlotTests()
    {
        var sessions = new SessionManager();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        session = sessions.CreateSession(client, 1, 1);
        session.Hp = 100;
        data.GetItem(Filler).Returns(new ItemData { Num = Filler });
        data.GetItem(Reward).Returns(new ItemData { Num = Reward });
        data.GetItem(JobChangeRules.MasterChangeScroll)
            .Returns(new ItemData { Num = JobChangeRules.MasterChangeScroll, Countable = 1 });
        context = new QuestScriptContext(session, null, data, sessions, Substitute.For<ILogger>(), 1);
    }

    [Fact]
    public void AGiftIntoTheBackHalfOfTheBagNamesThatSlot()
    {
        FillBagExcept(BackHalfSlot);

        context.Items.GiveItem(0, Reward, 1).Should().BeTrue();

        session.Inventory[InventoryConstants.InventoryStart + BackHalfSlot].ItemId.Should().Be(Reward);
        CountChangePositions().Should().Equal((byte)BackHalfSlot);
    }

    [Fact]
    public void TakingFromTheBackHalfOfTheBagNamesThatSlot()
    {
        Place(BackHalfSlot, Reward);

        context.Items.RobItem(0, Reward, 1).Should().BeTrue();

        CountChangePositions().Should().Equal((byte)BackHalfSlot);
    }

    [Fact]
    public void ARewardWithNoRoomSaysTheInventoryIsFull()
    {
        FillBagExcept(NoFreeSlot);

        context.Items.ApplyScriptReward([], [(Reward, 1, 0)]).Should().BeFalse();

        context.FailureReason.Should().Be(ScriptItemService.InventoryFullReason);
        session.Inventory.Should().NotContain(slot => slot.ItemId == Reward);
    }

    [Fact]
    public void AGiftWithNoRoomSaysTheInventoryIsFull()
    {
        FillBagExcept(NoFreeSlot);

        context.Items.GiveItem(0, Reward, 1).Should().BeFalse();

        context.FailureReason.Should().Be(ScriptItemService.InventoryFullReason);
    }

    [Fact]
    public void AJobChangeTokenInTheBackHalfOfTheBagNamesThatSlot()
    {
        session.Class = 101;
        session.Race = 1;
        session.Nation = AccountNation.Karus;
        Place(BackHalfSlot, JobChangeRules.MasterChangeScroll);

        context.Character.JobChange(0, changeType: 0, newJob: 5).Should().BeTrue();

        CountChangePositions().Should().Equal((byte)BackHalfSlot);
    }

    private void Place(int bagSlot, int itemId)
    {
        var slot = session.Inventory[InventoryConstants.InventoryStart + bagSlot];
        slot.ItemId = itemId;
        slot.Count = 1;
    }

    private void FillBagExcept(int freeSlot)
    {
        for (var bagSlot = 0; bagSlot < InventoryConstants.HaveMax; bagSlot++)
            if (bagSlot != freeSlot)
                Place(bagSlot, Filler);
    }

    private List<byte> CountChangePositions() => CountChangePackets.Positions(context.QueuedPackets);
}
