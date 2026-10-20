using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class InventoryJournalTests
{
    private const int PlayerId = 640;
    private const int Arrow = 391010000;
    private const int Potion = 389010000;
    private const int Bag = InventoryConstants.InventoryStart;
    private const int Purse = 1_000;
    private const int Price = 300;
    private const int Found = 500;
    private const ushort Stack = 10;
    private const ushort Moved = 3;
    private const ushort PickedUp = 5;

    [Fact]
    public void GoldThatArrivesBeforeAFailedChangeIsUndoneStays()
    {
        var session = Player();
        session.Money = Purse;
        var journal = new InventoryJournal(session);
        journal.Apply(_ => journal.AddMoney(-Price));
        session.Money += Found;

        session.WithLock(_ => journal.Restore());

        session.Money.Should().Be(Purse + Found, "the undo gives back the price, not the purse as it once was");
    }

    [Fact]
    public void GoldSpentFromAFailedCreditLeavesAnEmptyPurse()
    {
        var session = Player();
        var journal = new InventoryJournal(session);
        journal.Apply(_ => journal.AddMoney(Price));
        session.Money -= Price - Moved;

        session.WithLock(_ => journal.Restore());

        session.Money.Should().Be(0);
    }

    [Fact]
    public void AnUndoNeverFillsThePursePastItsCap()
    {
        var session = Player();
        session.Money = ExchangePacketConstants.CoinMax;
        var journal = new InventoryJournal(session);
        journal.Apply(_ => journal.AddMoney(-Price));
        session.Money += Price;

        session.WithLock(_ => journal.Restore());

        session.Money.Should().Be(ExchangePacketConstants.CoinMax);
    }

    [Fact]
    public void AStackKeepsWhatJoinedItAfterAFailedChange()
    {
        var session = Player();
        Put(session, Arrow, Stack);
        var journal = new InventoryJournal(session);
        journal.Apply(_ => journal.Slot(Bag).Count -= Moved);
        session.Inventory[Bag].Count += PickedUp;

        session.WithLock(_ => journal.Restore());

        session.Inventory[Bag].Count.Should().Be(Stack + PickedUp, "the undo hands back the arrows it took and keeps the ones picked up");
    }

    [Fact]
    public void AnUndoNeverGrowsAStackPastItsCap()
    {
        var session = Player();
        Put(session, Arrow, InventoryConstants.MaxStackCount);
        var journal = new InventoryJournal(session);
        journal.Apply(_ => journal.Slot(Bag).Count -= Moved);
        session.Inventory[Bag].Count += Moved;

        session.WithLock(_ => journal.Restore());

        session.Inventory[Bag].Count.Should().Be(InventoryConstants.MaxStackCount);
    }

    [Fact]
    public void ADeliveredStackSpentFromIsTakenBackWhole()
    {
        var session = Player();
        var journal = new InventoryJournal(session);
        journal.Apply(_ =>
        {
            var slot = journal.Slot(Bag);
            (slot.ItemId, slot.Count, slot.Durability) = (Arrow, Stack, 1);
        });
        session.Inventory[Bag].Count -= Moved;

        session.WithLock(_ => journal.Restore());

        session.Inventory[Bag].IsEmpty.Should().BeTrue("a delivery that was never saved leaves nothing behind");
    }

    [Fact]
    public void AnItemPutWhereTheChangeTookOneIsLeftAlone()
    {
        var session = Player();
        Put(session, Potion, 1);
        var journal = new InventoryJournal(session);
        journal.Apply(_ => journal.TakeOne(Potion));
        Put(session, Arrow, Stack);

        session.WithLock(_ => journal.Restore());

        session.Inventory[Bag].ItemId.Should().Be(Arrow, "the undo does not overwrite an item that arrived after the change");
    }

    [Fact]
    public void ASlotTheChangeTurnedIntoAnotherItemAndThatChangedSinceIsLeftAlone()
    {
        var session = Player();
        Put(session, Potion, 1);
        var journal = new InventoryJournal(session);
        journal.Apply(_ =>
        {
            var slot = journal.Slot(Bag);
            (slot.ItemId, slot.Count) = (Arrow, Stack);
        });
        session.Inventory[Bag].Count -= Moved;

        session.WithLock(_ => journal.Restore());

        session.Inventory[Bag].ItemId.Should().Be(Arrow);
        session.Inventory[Bag].Count.Should().Be(Stack - Moved);
    }

    [Fact]
    public void AnUntouchedResultIsPutBackWhole()
    {
        var session = Player();
        Put(session, Potion, 1);
        var journal = new InventoryJournal(session);
        journal.Apply(_ => journal.TakeOne(Potion));

        session.WithLock(_ => journal.Restore());

        session.Inventory[Bag].ItemId.Should().Be(Potion);
        session.Inventory[Bag].Count.Should().Be(1);
    }

    private static void Put(UserSession session, int itemId, ushort count)
    {
        var slot = session.Inventory[Bag];
        (slot.ItemId, slot.Count, slot.Durability) = (itemId, count, 1);
    }

    private static UserSession Player() => new(Substitute.For<IClient>(), PlayerId, PlayerId);
}
