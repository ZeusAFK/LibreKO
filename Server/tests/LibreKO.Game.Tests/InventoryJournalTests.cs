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
    private const int Scroll = 379016000;
    private const int Bag = InventoryConstants.InventoryStart;
    private const int Purse = 1_000;
    private const int Price = 300;
    private const int Found = 500;
    private const ushort Stack = 10;
    private const ushort Moved = 3;
    private const ushort PickedUp = 5;
    private const short Worn = 5;
    private const short Repaired = 50;
    private const int TwoPotions = 2;
    private const ushort Topped = 3;
    private const ushort Used = 4;

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
        Total(session, Arrow).Should().Be(InventoryConstants.MaxStackCount + Moved, "what the change took goes back beside a full stack");
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
        session.Inventory.Where(slot => slot.ItemId == Potion).Sum(slot => slot.Count).Should().Be(1, "the potion the change took comes back elsewhere");
    }

    [Fact]
    public void AChangeWhoseOutputWasPartlySpentStands()
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

        Total(session, Arrow).Should().Be(Stack - Moved, "the potion paid for the arrows, and some of them were spent");
        Total(session, Potion).Should().Be(0, "the potion does not come back on top of the arrows");
    }

    [Fact]
    public void GoldPaidIsNotHandedBackWhenWhatItBoughtLeftTheBag()
    {
        var session = Player();
        session.Money = Purse;
        var journal = new InventoryJournal(session);
        journal.Apply(_ =>
        {
            journal.AddMoney(-Price);
            journal.DeliverAll(new ItemData { Num = Arrow, Countable = 1, Duration = 1 }, Stack);
        });
        InventoryJournal.CopyInto(session.Inventory[Bag], session.Warehouse[0]);
        session.Inventory[Bag].Clear();

        session.WithLock(_ => journal.Restore());

        session.Money.Should().Be(Purse - Price, "the arrows the gold paid for are still the player's");
        session.Warehouse[0].Count.Should().Be(Stack);
    }

    [Fact]
    public void JournalsOfOneExchangeGoBackTogetherOrNotAtAll()
    {
        var buyer = Player();
        var seller = Player();
        buyer.Money = Purse;
        Put(seller, Arrow, Stack);
        var bought = new InventoryJournal(buyer);
        var sold = new InventoryJournal(seller);
        bought.Apply(_ =>
        {
            bought.AddMoney(-Price);
            bought.DeliverAll(new ItemData { Num = Arrow, Countable = 1, Duration = 1 }, Stack);
        });
        sold.Apply(_ =>
        {
            sold.AddMoney(Price);
            sold.Slot(Bag).Clear();
        });
        InventoryJournal.CopyInto(buyer.Inventory[Bag], buyer.Warehouse[0]);
        buyer.Inventory[Bag].Clear();

        UserSession.WithBoth(buyer, seller, (_, _) => InventoryJournal.RestoreTogether(bought, sold));

        Total(seller, Arrow).Should().Be(0, "the arrows are still the buyer's, so the sale stands for the seller too");
        seller.Money.Should().Be(Price);
        buyer.Money.Should().Be(Purse - Price);
    }

    [Fact]
    public void GoodsSoldAreNotHandedBackWhenTheGoldTheyFetchedWasSpent()
    {
        var session = Player();
        Put(session, Arrow, Stack);
        var journal = new InventoryJournal(session);
        journal.Apply(_ =>
        {
            journal.AddMoney(Price);
            journal.Slot(Bag).Clear();
        });
        session.Money -= Price;

        session.WithLock(_ => journal.Restore());

        Total(session, Arrow).Should().Be(0, "the gold the arrows fetched is spent, so the sale stands");
        session.Money.Should().Be(0);
    }

    [Fact]
    public void ATopUpThatAlsoRaisedDurabilityTakesBackOnlyWhatItAdded()
    {
        var session = Player();
        Put(session, Potion, PickedUp);
        var journal = new InventoryJournal(session);
        journal.Apply(_ =>
        {
            var slot = journal.Slot(Bag);
            slot.Count += Topped;
            slot.Durability++;
        });
        session.Inventory[Bag].Count -= Used;

        session.WithLock(_ => journal.Restore());

        session.Inventory[Bag].Count.Should().Be(PickedUp + Topped - Used - Topped, "only the potions the change added are taken back");
        session.Inventory[Bag].Durability.Should().Be(1);
    }

    [Fact]
    public void WhatAChangeMadeIsTakenBackFromAMagicBag()
    {
        var session = Player();
        var journal = new InventoryJournal(session);
        journal.Apply(_ => journal.DeliverAll(new ItemData { Num = Arrow, Countable = 1, Duration = 1 }, Stack));
        InventoryJournal.CopyInto(session.Inventory[Bag], session.Inventory[InventoryConstants.MagicBagStart]);
        session.Inventory[Bag].Clear();

        session.WithLock(_ => journal.Restore());

        Total(session, Arrow).Should().Be(0, "a magic bag is carried too");
    }

    [Fact]
    public void WhatFindsNoRoomAnywhereIsLogged()
    {
        var session = Player();
        Put(session, Potion, 1);
        var log = new ErrorLog();
        var journal = new InventoryJournal(session, log);
        journal.Apply(_ => journal.TakeOne(Potion));
        for (var index = Bag; index < Bag + InventoryConstants.HaveMax; index++)
            (session.Inventory[index].ItemId, session.Inventory[index].Count) = (Scroll, 1);
        foreach (var slot in session.Warehouse)
            (slot.ItemId, slot.Count) = (Scroll, 1);

        session.WithLock(_ => journal.Restore());

        log.Errors.Should().ContainSingle(error => error.Contains(Potion.ToString()), "an item the undo cannot place anywhere is lost, so it is logged");
    }

    private sealed class ErrorLog : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }

    [Fact]
    public void WhatFindsNoRoomInTheBagGoesToTheWarehouse()
    {
        var session = Player();
        Put(session, Potion, 1);
        var journal = new InventoryJournal(session);
        journal.Apply(_ => journal.TakeOne(Potion));
        for (var index = Bag; index < Bag + InventoryConstants.HaveMax; index++)
            (session.Inventory[index].ItemId, session.Inventory[index].Count) = (Scroll, 1);

        session.WithLock(_ => journal.Restore());

        session.Warehouse.Should().ContainSingle(slot => slot.ItemId == Potion, "a full bag does not swallow what the undo hands back");
    }

    [Fact]
    public void WhatAChangeMadeIsTakenBackWhereverItWent()
    {
        var session = Player();
        Put(session, Potion, 1);
        var journal = new InventoryJournal(session);
        journal.Apply(_ =>
        {
            var slot = journal.Slot(Bag);
            (slot.ItemId, slot.Count) = (Arrow, Stack);
        });
        InventoryJournal.CopyInto(session.Inventory[Bag], session.Inventory[Bag + 1]);
        session.Inventory[Bag].Clear();
        Put(session, Scroll, 1);

        session.WithLock(_ => journal.Restore());

        Total(session, Arrow).Should().Be(0, "the arrows were moved, not spent");
        Total(session, Potion).Should().Be(1);
        session.Inventory[Bag].ItemId.Should().Be(Scroll, "the undo does not overwrite an item that arrived after the change");
    }

    [Fact]
    public void AnItemAChangeMovedIsNotHandedBackTwice()
    {
        var session = Player();
        Put(session, Potion, 1);
        var journal = new InventoryJournal(session);
        journal.Apply(_ =>
        {
            InventoryJournal.CopyInto(journal.Slot(Bag), journal.Slot(Bag + 1));
            journal.Slot(Bag).Clear();
        });
        InventoryJournal.CopyInto(session.Inventory[Bag + 1], session.Inventory[Bag + 2]);
        session.Inventory[Bag + 1].Clear();
        Put(session, Arrow, Stack);

        session.WithLock(_ => journal.Restore());

        Total(session, Potion).Should().Be(1, "the potion never left the bag");
        Total(session, Arrow).Should().Be(Stack);
    }

    [Fact]
    public void ASingleItemGoesBackBesideAnotherOfItsKind()
    {
        var session = Player();
        Put(session, Potion, 1);
        var journal = new InventoryJournal(session);
        journal.Apply(_ => journal.TakeOne(Potion));
        Put(session, Potion, 1);

        session.WithLock(_ => journal.Restore());

        session.Inventory[Bag].Count.Should().Be(1, "only a stack the change cut takes items back onto itself");
        Total(session, Potion).Should().Be(TwoPotions);
    }

    [Fact]
    public void WhatAChangeMadeComesOutOfItsOwnSlotFirst()
    {
        var session = Player();
        Put(session, Potion, 1);
        session.Inventory[Bag].Durability = Repaired;
        var journal = new InventoryJournal(session);
        journal.Apply(_ =>
        {
            var slot = journal.Slot(Bag + 1);
            (slot.ItemId, slot.Count, slot.Durability) = (Potion, 1, Worn);
        });

        session.WithLock(_ => journal.Restore());

        session.Inventory[Bag].Durability.Should().Be(Repaired, "the player's own potion is not the one taken back");
        session.Inventory[Bag + 1].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AnItemTheChangeWoreDownGetsItsDurabilityBack()
    {
        var session = Player();
        Put(session, Potion, 1);
        session.Inventory[Bag].Durability = Repaired;
        var journal = new InventoryJournal(session);
        journal.Apply(_ => journal.Slot(Bag).Durability = Worn);

        session.WithLock(_ => journal.Restore());

        session.Inventory[Bag].Durability.Should().Be(Repaired);
        Total(session, Potion).Should().Be(1);
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

    private static int Total(UserSession session, int itemId) =>
        session.Inventory.Where(slot => slot.ItemId == itemId).Sum(slot => slot.Count);

    private static UserSession Player() => new(Substitute.For<IClient>(), PlayerId, PlayerId);
}
