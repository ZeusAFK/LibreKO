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

    private readonly IGameDataService _data = Substitute.For<IGameDataService>();
    private readonly UserSession _session;
    private readonly QuestScriptContext _context;

    public QuestRewardSlotTests()
    {
        var sessions = new SessionManager();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        _session = sessions.CreateSession(client, 1, 1);
        _session.Hp = 100;
        _data.GetItem(Filler).Returns(new ItemData { Num = Filler });
        _data.GetItem(Reward).Returns(new ItemData { Num = Reward });
        _data.GetItem(JobChangeRules.MasterChangeScroll)
            .Returns(new ItemData { Num = JobChangeRules.MasterChangeScroll, Countable = 1 });
        _context = new QuestScriptContext(_session, null, _data, sessions, Substitute.For<ILogger>(), 1);
    }

    [Fact]
    public void AGiftIntoTheBackHalfOfTheBagNamesThatSlot()
    {
        FillBagExcept(BackHalfSlot);

        _context.Items.GiveItem(0, Reward, 1).Should().BeTrue();

        _session.Inventory[InventoryConstants.InventoryStart + BackHalfSlot].ItemId.Should().Be(Reward);
        CountChangePositions().Should().Equal((byte)BackHalfSlot);
    }

    [Fact]
    public void TakingFromTheBackHalfOfTheBagNamesThatSlot()
    {
        Place(BackHalfSlot, Reward);

        _context.Items.RobItem(0, Reward, 1).Should().BeTrue();

        CountChangePositions().Should().Equal((byte)BackHalfSlot);
    }

    [Fact]
    public void ARewardWithNoRoomSaysTheInventoryIsFull()
    {
        FillBagExcept(NoFreeSlot);

        _context.Items.ApplyScriptReward([], [(Reward, 1, 0)]).Should().BeFalse();

        _context.FailureReason.Should().Be(ScriptItemService.InventoryFullReason);
        _session.Inventory.Should().NotContain(slot => slot.ItemId == Reward);
    }

    [Fact]
    public void AGiftWithNoRoomSaysTheInventoryIsFull()
    {
        FillBagExcept(NoFreeSlot);

        _context.Items.GiveItem(0, Reward, 1).Should().BeFalse();

        _context.FailureReason.Should().Be(ScriptItemService.InventoryFullReason);
    }

    [Fact]
    public void AJobChangeTokenInTheBackHalfOfTheBagNamesThatSlot()
    {
        _session.Class = 101;
        _session.Race = 1;
        _session.Nation = AccountNation.Karus;
        Place(BackHalfSlot, JobChangeRules.MasterChangeScroll);

        _context.Character.JobChange(0, changeType: 0, newJob: 5).Should().BeTrue();

        CountChangePositions().Should().Equal((byte)BackHalfSlot);
    }

    private void Place(int bagSlot, int itemId)
    {
        var slot = _session.Inventory[InventoryConstants.InventoryStart + bagSlot];
        slot.ItemId = itemId;
        slot.Count = 1;
    }

    private void FillBagExcept(int freeSlot)
    {
        for (var bagSlot = 0; bagSlot < InventoryConstants.HaveMax; bagSlot++)
            if (bagSlot != freeSlot)
                Place(bagSlot, Filler);
    }

    private List<byte> CountChangePositions() => CountChangePackets.Positions(_context.QueuedPackets);
}
