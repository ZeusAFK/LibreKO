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
    private const int Token = JobChangeRules.MasterChangeScroll;
    private const ushort Held = 50;
    private const ushort Given = 10;
    private const int RentalHours = 24;
    private const long RentalEnds = 4_102_444_800;
    private const int Purse = 1000;
    private const int Fee = 100;
    private const int Ore = 389_043_000;
    private const byte OreType = 2;
    private const short Pitman = 31511;
    private const int EveryTime = 10000 * 5;
    private const ushort Ores = 2;

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

    [Fact]
    public void ARentedGiftLeavesThePermanentStackBesideItAlone()
    {
        Place(0, Token);
        Bag(0).Count = Held;

        context.Items.GiveItem(0, Token, Given, RentalHours).Should().BeTrue();

        Bag(0).Count.Should().Be(Held);
        Bag(0).Expires.Should().BeFalse("a rental reward must not make the tokens the player already owned expire");
        var rented = session.Inventory.Single(slot => slot.ItemId == Token && slot.Expires);
        rented.Count.Should().Be(Given);
        rented.State.Should().Be(ItemFlag.Rented);
    }

    [Fact]
    public void APlainGiftDoesNotJoinARentedStack()
    {
        Place(0, Token);
        Bag(0).Count = Held;
        Bag(0).ExpireInHours(RentalHours, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        context.Items.GiveItem(0, Token, Given).Should().BeTrue();

        Bag(0).Count.Should().Be(Held, "a lasting reward does not vanish with a rented stack");
        session.Inventory.Single(slot => slot.ItemId == Token && !slot.Expires).Count.Should().Be(Given);
    }

    [Fact]
    public void ARewardDoesNotCountARentedStackAsRoomAndTakesNothing()
    {
        FillBagExcept(0);
        Place(0, Token);
        Bag(0).ExpiresAt = RentalEnds;
        session.Money = Purse;

        context.Items.ApplyScriptReward([(InventoryConstants.ItemGold, Fee)], [(Token, 1, 0)]).Should().BeFalse();

        session.Money.Should().Be(Purse, "nothing is taken for a reward that cannot be given");
        Bag(0).Count.Should().Be(1);
    }

    [Fact]
    public void AMiningExchangeDoesNotCountARentedStackAsRoomAndKeepsTheOre()
    {
        FillBagExcept(0);
        Place(0, Token);
        Bag(0).ExpiresAt = RentalEnds;
        Place(1, Ore);
        Bag(1).Count = Ores;
        data.GetItem(Ore).Returns(new ItemData { Num = Ore, Countable = 1 });
        data.MiningExchangesByOreNpc.Returns(new[]
        {
            new MiningExchangeData
            {
                NpcId = Pitman, OreType = OreType, OriginItemNum = Ore, GiveItemNum = Token, GiveItemCount = 1,
                SuccessRate = EveryTime,
            },
        }.ToLookup(entry => (entry.OreType, entry.NpcId)));

        context.Items.RunMiningExchange(0, OreType).Should().BeFalse();

        Bag(1).Count.Should().Be(Ores, "the ore is not taken for a reward that has no room");
        Bag(0).Count.Should().Be(1);
    }

    private ItemSlot Bag(int bagSlot) => session.Inventory[InventoryConstants.InventoryStart + bagSlot];

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
