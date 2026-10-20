using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using LibreKO.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using System.Linq.Expressions;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class CharacterSealTests : GameTestBase
{
    private const int AccountA = 1;
    private const int AccountB = 2;
    private const int Scroll = 800111000;
    private const byte BagPos = 3;
    private const string Pin = "1234";

    private const short PlayerHp = 100;
    private const int Plain = 389010000;
    private const int TradeLocked = 389020000;
    private const int Unknown = 389030000;
    private const int NoTradeNumber = 900_100_000;
    private const byte TradeLockedRace = 70;
    private const int PetSerial = 31;
    private const int CarriedAt = InventoryConstants.InventoryStart;
    private const int SenderPurse = 100_000;
    private const int MailedGold = 100;
    private const int GiftCatalogId = 1;
    private const int GiftPrice = 100;
    private static readonly TimeSpan SendWait = TimeSpan.FromMilliseconds(300);

    public enum Carried { Bound, Rented, Timed, TradeLockedData, NoTradeNumber, PetLink, UnknownItem }

    public enum Held { Mail, Letter, AuctionBid, SlaveMerchant }

    [Theory]
    [InlineData(Held.Mail)]
    [InlineData(Held.Letter)]
    [InlineData(Held.AuctionBid)]
    [InlineData(Held.SlaveMerchant)]
    public async Task ACharacterWithSomethingWaitingOutsideTheBagIsNotSealed(Held what)
    {
        using var provider = Provider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            switch (what)
            {
                case Held.Mail:
                    db.Mails.Add(new Mail
                    {
                        RecipientCharacterId = 11, Subject = "Gift", SentAt = DateTime.UtcNow,
                        Attachments = [new MailAttachment { Kind = MailAttachmentKind.Item, ItemId = TradeLocked, Count = 1 }],
                    });
                    break;
                case Held.Letter:
                    db.MailBoxes.Add(new MailBox
                    {
                        RecipientId = "Velkan", SenderId = "Shop", Subject = "Gift", Message = "Gift", SendDate = DateTime.UtcNow,
                        Status = ShoppingMallLetterProtocol.LetterStatusNew, Type = ShoppingMallPacketWriter.LetterTypeWithItem, ItemId = TradeLocked, Count = 1,
                    });
                    break;
                case Held.AuctionBid:
                    db.AuctionBids.Add(new AuctionBid { CharacterId = 11, CharacterName = "Velkan", Status = AuctionBidStatus.Won, PlacedAt = DateTime.UtcNow });
                    break;
                case Held.SlaveMerchant:
                    db.SlaveMerchants.Add(new SlaveMerchantData { OwnerCharacterId = 11, OwnerAccountId = AccountA, OwnerName = "Velkan", BotName = "Velkan's Slave" });
                    break;
            }

            await db.SaveChangesAsync();
        }

        var session = Session(provider, 10, AccountA, "Zeus");
        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        session.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId.Should().Be(Scroll,
            "what waits for the character outside its bag would change hands with the ring");
    }

    private static ServiceProvider Provider(Action<IServiceCollection>? configureServices = null) => CreateProvider(db =>
    {
        db.Accounts.Add(new Account { Id = AccountA, Login = "a", Password = "x", Nation = AccountNation.Karus });
        db.Accounts.Add(new Account { Id = AccountB, Login = "b", Password = "x", Nation = AccountNation.Karus });
        db.Characters.Add(new Character { Id = 10, AccountId = AccountA, Slot = 0, Name = "Zeus", Race = 1, Class = 101, Level = 60, MapId = 21 });
        db.Characters.Add(new Character { Id = 11, AccountId = AccountA, Slot = 1, Name = "Velkan", Race = 1, Class = 105, Level = 70, MapId = 21 });
        db.Characters.Add(new Character { Id = 20, AccountId = AccountB, Slot = 0, Name = "Buyer", Race = 1, Class = 101, Level = 60, MapId = 21 });
    }, gameData =>
    {
        gameData.GetItem(Scroll).Returns(new ItemData { Num = Scroll, Kind = CharacterSealService.ScrollKind });
        gameData.GetItem(InventoryConstants.CypherRing).Returns(new ItemData { Num = InventoryConstants.CypherRing, Kind = CharacterSealService.RingKind, Duration = 1 });
        gameData.GetItem(Plain).Returns(new ItemData { Num = Plain, Duration = 1 });
        gameData.GetItem(TradeLocked).Returns(new ItemData { Num = TradeLocked, Duration = 1, Race = TradeLockedRace });
        gameData.GetItem(NoTradeNumber).Returns(new ItemData { Num = NoTradeNumber, Duration = 1 });
    }, configureServices: configureServices);

    [Theory]
    [InlineData(Carried.Bound, CharacterSealService.BoundItemRefusal)]
    [InlineData(Carried.Rented, CharacterSealService.RentalItemRefusal)]
    [InlineData(Carried.Timed, CharacterSealService.RentalItemRefusal)]
    [InlineData(Carried.TradeLockedData, CharacterSealService.BoundItemRefusal)]
    [InlineData(Carried.NoTradeNumber, CharacterSealService.BoundItemRefusal)]
    [InlineData(Carried.PetLink, CharacterSealService.BoundItemRefusal)]
    [InlineData(Carried.UnknownItem, CharacterSealService.BoundItemRefusal)]
    public async Task ACharacterCarryingWhatCouldNotBeTradedIsNotSealed(Carried what, short refusal)
    {
        using var provider = Provider();
        var item = new ItemSlot { ItemId = Plain, Count = 1, Durability = 1 };
        switch (what)
        {
            case Carried.Bound:
                item.Flag = (byte)ItemFlag.Bound;
                break;
            case Carried.Rented:
                item.ExpireInHours(24, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                break;
            case Carried.Timed:
                item.ExpiresAt = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
                break;
            case Carried.TradeLockedData:
                item.ItemId = TradeLocked;
                break;
            case Carried.NoTradeNumber:
                item.ItemId = NoTradeNumber;
                break;
            case Carried.PetLink:
                item.UniqueId = PetSerial;
                break;
            case Carried.UnknownItem:
                item.ItemId = Unknown;
                break;
        }
        await CarryAsync(provider, item);
        var session = Session(provider, 10, AccountA, "Zeus");
        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        session.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId.Should().Be(Scroll, "a ring would carry the item past the trade lock");
        var reply = LastSent(session);
        while (reply.RemainingBytes > sizeof(short))
            reply.ReadByte();
        reply.ReadShort().Should().Be(refusal);
    }

    [Fact]
    public async Task ACharacterCarryingOnlyTradableItemsIsSealed()
    {
        using var provider = Provider();
        await CarryAsync(provider, new ItemSlot { ItemId = Plain, Count = 1, Durability = 1 });
        var session = Session(provider, 10, AccountA, "Zeus");
        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        session.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId.Should().Be(InventoryConstants.CypherRing);
    }

    private static async Task CarryAsync(ServiceProvider provider, ItemSlot item)
    {
        var items = new ItemSlot[InventoryConstants.InventoryTotal];
        for (var i = 0; i < items.Length; i++)
            items[i] = new ItemSlot();
        items[CarriedAt] = item;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Characters.SingleAsync(c => c.Name == "Velkan")).Items = UserSessionBinaryState.SerializeItems(items);
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheRingAndTheSealedCharacterAreSavedTogether(bool saveFails)
    {
        var faults = new SealFaults { Failing = saveFails };
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var session = Session(provider, 10, AccountA, "Zeus");
        var scroll = session.Inventory[InventoryConstants.InventoryStart + BagPos];
        scroll.ItemId = Scroll;
        scroll.Count = 1;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sealedCharacter = await db.Characters.AsNoTracking().SingleAsync(c => c.Id == 11);
        var sealer = await db.Characters.AsNoTracking().SingleAsync(c => c.Id == 10);
        sealedCharacter.Sealed.Should().Be(!saveFails, "a crash between the two writes would lose the character or keep the scroll");
        scroll.ItemId.Should().Be(saveFails ? Scroll : InventoryConstants.CypherRing);
        if (!saveFails)
            sealer.Items.Should().Equal(session.SerializeItems());
    }

    [Fact]
    public async Task ABusyPlayerCannotSeal()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        var scroll = session.Inventory[InventoryConstants.InventoryStart + BagPos];
        scroll.ItemId = Scroll;
        scroll.Count = 1;
        session.Trade.MerchantState = MerchantMode.Selling;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        scroll.ItemId.Should().Be(Scroll, "a scroll on an open stall can be bought while the seal waits on the database");
    }

    [Fact]
    public async Task AScrollThatLeavesTheBagMidSealMakesNoRing()
    {
        UserSession? sealer = null;
        var sale = new DuringCharacterRead { Meanwhile = () => sealer!.Inventory[InventoryConstants.InventoryStart + BagPos].Clear() };
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(sale)));
        sealer = Session(provider, 10, AccountA, "Zeus");
        var scroll = sealer.Inventory[InventoryConstants.InventoryStart + BagPos];
        scroll.ItemId = Scroll;
        scroll.Count = 1;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(sealer, SealRequest("Velkan", Pin));

        scroll.IsEmpty.Should().BeTrue("the scroll went to a buyer, so no ring is made from it");
        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Characters.AsNoTracking().SingleAsync(c => c.Id == 11))
            .Sealed.Should().BeFalse();
    }

    [Fact]
    public async Task AnUnsealWhoseSaveFailsKeepsTheRingAndTheSeal()
    {
        var faults = new UnsealFaults();
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var (buyer, ring) = await SealedVelkanAsync(provider);

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(buyer, UnsealRequest(InventoryConstants.CypherRing, 2));

        ring.ItemId.Should().Be(InventoryConstants.CypherRing, "the ring and the character are saved together or not at all");
        ring.UniqueId.Should().Be(11);
        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Characters.AsNoTracking().SingleAsync(c => c.Id == 11))
            .Sealed.Should().BeTrue();
    }

    [Fact]
    public async Task ABusyPlayerCannotUnseal()
    {
        using var provider = Provider();
        var (buyer, ring) = await SealedVelkanAsync(provider);
        buyer.Trade.MerchantState = MerchantMode.Selling;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(buyer, UnsealRequest(InventoryConstants.CypherRing, 2));

        ring.ItemId.Should().Be(InventoryConstants.CypherRing, "a ring on an open stall can be bought while the unseal waits on the database");
    }

    [Fact]
    public async Task ARingThatLeavesTheBagMidUnsealFreesNoOne()
    {
        UserSession? buyer = null;
        var armed = false;
        var sale = new DuringCharacterRead { Meanwhile = () => { if (armed) buyer!.Inventory[InventoryConstants.InventoryStart + BagPos].Clear(); } };
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(sale)));
        (buyer, _) = await SealedVelkanAsync(provider);
        armed = true;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(buyer, UnsealRequest(InventoryConstants.CypherRing, 2));

        using var scope = provider.CreateScope();
        var velkan = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Characters.AsNoTracking().SingleAsync(c => c.Id == 11);
        velkan.Sealed.Should().BeTrue("the ring went to a buyer, so it frees no character");
        velkan.AccountId.Should().Be(AccountA);
    }

    [Theory]
    [InlineData(RingAt.Nowhere, false)]
    [InlineData(RingAt.Bag, true)]
    [InlineData(RingAt.PartnersOffer, true)]
    [InlineData(RingAt.AskedPartnersOffer, false)]
    public async Task ASealedCharactersSheetIsShownOnlyForARingThePlayerCanSee(RingAt where, bool shown)
    {
        using var provider = Provider();
        var (holder, ring) = await SealedVelkanAsync(provider);
        var viewer = Session(provider, 30, AccountB, "Viewer");
        switch (where)
        {
            case RingAt.Bag:
                InventoryJournal.CopyInto(ring, viewer.Inventory[InventoryConstants.InventoryStart]);
                break;
            case RingAt.PartnersOffer:
                holder.Trade.ExchangeItemList.Add(new ExchangeItem { ItemId = InventoryConstants.CypherRing, Count = 1, UniqueId = 11 });
                holder.Trade.ExchangeUser = viewer.CharacterId;
                viewer.Trade.ExchangeUser = holder.CharacterId;
                holder.Trade.ExchangeAccepted = viewer.Trade.ExchangeAccepted = true;
                break;
            case RingAt.AskedPartnersOffer:
                holder.Trade.ExchangeItemList.Add(new ExchangeItem { ItemId = InventoryConstants.CypherRing, Count = 1, UniqueId = 11 });
                holder.Trade.ExchangeUser = Stranger;
                holder.Trade.ExchangeAccepted = true;
                viewer.Trade.ExchangeUser = holder.CharacterId;
                break;
        }

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(viewer, SheetRequest(11));

        var reply = LastSent(viewer);
        reply.ReadByte().Should().Be(CharacterSealPacketWriter.SealSub);
        reply.ReadByte().Should().Be(CharacterSealPacketWriter.SheetSub);
        (reply.ReadByte() == CharacterSealPacketWriter.Succeeded).Should().Be(shown, "a sealed character's bag and gold are no one else's business");
    }

    public enum RingAt { Nowhere, Bag, PartnersOffer, AskedPartnersOffer }

    private const int Stranger = 40;

    private static Packet SheetRequest(int uniqueId)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte(CharacterSealPacketWriter.SheetSub);
        packet.WriteInt(uniqueId);
        packet.ResetOffset();
        return packet;
    }

    private static async Task<(UserSession Buyer, ItemSlot Ring)> SealedVelkanAsync(ServiceProvider provider)
    {
        var sealer = Session(provider, 10, AccountA, "Zeus");
        sealer.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        sealer.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;
        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(sealer, SealRequest("Velkan", Pin));
        var buyer = Session(provider, 20, AccountB, "Buyer");
        var ring = buyer.Inventory[InventoryConstants.InventoryStart + BagPos];
        ring.ItemId = InventoryConstants.CypherRing;
        ring.Count = 1;
        ring.UniqueId = 11;
        return (buyer, ring);
    }

    private sealed class UnsealFaults : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            eventData.Context?.ChangeTracker.Entries<Character>().Any(entry => !entry.Entity.Sealed
                && entry.State == EntityState.Modified && entry.Property(nameof(Character.Sealed)).IsModified) == true
                ? throw new DbUpdateException("the database went away")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public enum Sent { Mail, Letter, Gift }

    [Theory]
    [InlineData(Sent.Mail)]
    [InlineData(Sent.Letter)]
    [InlineData(Sent.Gift)]
    public async Task WhatIsSentWhileTheCharacterIsBeingSealedWaitsAndIsRefused(Sent what)
    {
        UserSession? sender = null;
        IServiceProvider? services = null;
        Task sending = Task.CompletedTask;
        var during = new DuringSealSave
        {
            Meanwhile = () =>
            {
                sending = what switch
                {
                    Sent.Mail => services!.GetRequiredService<IMailService>().SendAsync(sender!, "Velkan", "Gift", "Gift", MailedGold, []),
                    Sent.Letter => services!.GetRequiredService<IShoppingMallLetterMutationService>().HandleSendAsync(sender!, LetterRequest("Velkan")),
                    _ => services!.GetRequiredService<IShoppingMallStoreService>().HandlePurchaseAsync(sender!, GiftRequest("Velkan")),
                };
                return Task.WhenAny(sending, Task.Delay(SendWait));
            },
        };
        using var provider = Provider(collection => collection.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(during)));
        services = provider;
        sender = Session(provider, 20, AccountB, "Buyer");
        sender.Money = SenderPurse;
        sender.KnightCash = SenderPurse;
        await using (var seeding = provider.CreateAsyncScope())
        {
            var seed = seeding.ServiceProvider.GetRequiredService<AppDbContext>();
            seed.PusItems.Add(new PusItemData { Id = GiftCatalogId, ItemId = Plain, Price = GiftPrice, Category = 1 });
            seed.Friendships.Add(new Friendship { CharacterId = 20, FriendCharacterId = 11, AddedAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }

        var sealer = Session(provider, 10, AccountA, "Zeus");
        sealer.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        sealer.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(sealer, SealRequest("Velkan", Pin));
        await sending;

        sealer.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId.Should().Be(CharacterSealService.CypherRing);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Mails.CountAsync(mail => mail.RecipientCharacterId == 11)).Should().Be(0, "nothing reaches a character between its seal's check and its save");
        (await db.MailBoxes.CountAsync(letter => letter.RecipientId == "Velkan")).Should().Be(0);
        sender.Money.Should().Be(SenderPurse, "what was refused cost nothing");
        sender.KnightCash.Should().Be(SenderPurse);
    }

    private static Packet GiftRequest(string recipient)
    {
        var packet = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        packet.WriteByte(ShoppingMallStoreService.PurchaseCart);
        packet.WriteSByteString(recipient);
        packet.WriteByte(1);
        packet.WriteInt(GiftCatalogId);
        packet.WriteUShort(1);
        packet.WriteInt(GiftPrice);
        packet.ResetOffset();
        return packet;
    }

    private static Packet LetterRequest(string recipient)
    {
        var packet = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        packet.WriteSByteString(recipient);
        packet.WriteSByteString("Gift");
        packet.WriteByte(ShoppingMallPacketWriter.LetterTypePlain);
        packet.WriteString("Gift");
        packet.ResetOffset();
        return packet;
    }

    private sealed class DuringSealSave : SaveChangesInterceptor
    {
        private int fired;

        public Func<Task> Meanwhile { get; init; } = () => Task.CompletedTask;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<Character>().Any(entry => entry.Entity.Sealed && entry.State == EntityState.Modified) == true
                && Interlocked.Exchange(ref fired, 1) == 0)
                await Meanwhile();
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class SealFaults : SaveChangesInterceptor
    {
        public bool Failing { get; init; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Failing && eventData.Context?.ChangeTracker.Entries<Character>().Any(entry => entry.Entity.Sealed && entry.State == EntityState.Modified) == true
                ? throw new DbUpdateException("the database went away")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private sealed class DuringCharacterRead : IQueryExpressionInterceptor
    {
        public Action Meanwhile { get; init; } = () => { };

        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            if (queryExpression.Type.IsGenericType && queryExpression.Type.GetGenericArguments().Contains(typeof(Character))
                || queryExpression.Type == typeof(Character)
                || queryExpression is MethodCallExpression { Arguments: [EntityQueryRootExpression root, ..] } && root.EntityType.ClrType == typeof(Character))
                Meanwhile();
            return queryExpression;
        }
    }

    private static UserSession Session(ServiceProvider provider, int characterId, int accountId, string name)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId, accountId);
        session.Name = name;
        session.Nation = AccountNation.Karus;
        session.VipPassword = Pin;
        session.Hp = session.MaxHp = PlayerHp;
        for (int i = 0; i < session.Inventory.Length; i++) session.Inventory[i] = new ItemSlot();
        return session;
    }

    private static Packet SealRequest(string name, string code)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte(CharacterSealPacketWriter.SealCharacterSub);
        packet.WriteInt(-1);
        packet.WriteByte(BagPos);
        packet.WriteInt(Scroll);
        packet.WriteString(name);
        packet.WriteString(code);
        packet.ResetOffset();
        return packet;
    }

    private static Packet UnsealRequest(int ringId, byte slot)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte(CharacterSealPacketWriter.UnsealSub);
        packet.WriteInt(-1);
        packet.WriteByte(BagPos);
        packet.WriteInt(ringId);
        packet.WriteByte(slot);
        packet.ResetOffset();
        return packet;
    }

    private static Packet LastSent(UserSession session)
    {
        var packet = session.Client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IClient.SendPacket))
            .Select(call => (Packet)call.GetArguments()[0]!)
            .Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_UPGRADE);
        packet.ResetOffset();
        return packet;
    }

    [Fact]
    public async Task SealingTurnsTheScrollIntoARingAndHidesTheCharacterFromSelect()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        session.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        var ring = session.Inventory[InventoryConstants.InventoryStart + BagPos];
        ring.ItemId.Should().Be(InventoryConstants.CypherRing);
        ring.UniqueId.Should().Be(11);
        ring.IsTradable.Should().BeTrue();
        ring.IsPetLink.Should().BeFalse();
        var reply = LastSent(session);
        reply.ReadByte().Should().Be(CharacterSealPacketWriter.SealSub);
        reply.ReadByte().Should().Be(CharacterSealPacketWriter.SealCharacterSub);
        reply.ReadByte().Should().Be(CharacterSealPacketWriter.Succeeded);
        using var scope = provider.CreateScope();
        var listed = await scope.ServiceProvider.GetRequiredService<ICharacterRepository>().GetCharactersByAccount(AccountA);
        listed.Select(c => c.Name).Should().Equal("Zeus");
    }

    [Fact]
    public async Task ARentedScrollSealsNoCharacter()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        var scroll = session.Inventory[InventoryConstants.InventoryStart + BagPos];
        scroll.ItemId = Scroll;
        scroll.Count = 1;
        scroll.ExpireInHours(24, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        scroll.ItemId.Should().Be(Scroll, "a rented ring would expire and leave its character sealed for good");
        using var scope = provider.CreateScope();
        var listed = await scope.ServiceProvider.GetRequiredService<ICharacterRepository>().GetCharactersByAccount(AccountA);
        listed.Select(c => c.Name).Should().Contain("Velkan");
    }

    [Fact]
    public async Task ASealedCharacterCannotBeSelectedByTheAccountThatSealedIt()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        session.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;
        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        using var scope = provider.CreateScope();
        var selected = await scope.ServiceProvider.GetRequiredService<IPreGameService>()
            .SelectCharacterAsync(AccountA, "a", "Velkan", 1);

        selected.CharacterId.Should().Be(0);
        selected.Packet.ReadByte().Should().NotBe((byte)SelectCharacterResult.Success);
    }

    [Fact]
    public async Task ASealedCharacterIsNotLoadedIntoTheGame()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        session.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;
        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.AccountId.Returns(AccountA);
        client.CharacterId.Returns(11);
        using var scope = provider.CreateScope();

        var loaded = await scope.ServiceProvider.GetRequiredService<IGameSessionInitializer>().InitializeAsync(client);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task AWrongVaultPasswordIsRefusedWithCode22()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        session.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", "9999"));

        var reply = LastSent(session);
        reply.ReadByte();
        reply.ReadByte();
        reply.ReadByte().Should().Be(CharacterSealPacketWriter.Failed);
        reply.ReadByte().Should().Be((byte)ItemSealResult.WrongVaultPassword);
    }

    [Fact]
    public async Task UnsealingMovesTheCharacterIntoAnEmptySlotOfAnotherAccount()
    {
        using var provider = Provider();
        var sealer = Session(provider, 10, AccountA, "Zeus");
        sealer.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        sealer.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;
        var seal = provider.GetRequiredService<ICharacterSealService>();
        await seal.HandleAsync(sealer, SealRequest("Velkan", Pin));

        var buyer = Session(provider, 20, AccountB, "Buyer");
        var ring = buyer.Inventory[InventoryConstants.InventoryStart + BagPos];
        ring.ItemId = InventoryConstants.CypherRing;
        ring.Count = 1;
        ring.UniqueId = 11;

        await seal.HandleAsync(buyer, UnsealRequest(InventoryConstants.CypherRing, 2));

        ring.IsEmpty.Should().BeTrue();
        using var scope = provider.CreateScope();
        var moved = await scope.ServiceProvider.GetRequiredService<ICharacterRepository>().GetById(11);
        moved!.AccountId.Should().Be(AccountB);
        moved.Slot.Should().Be(2);
        moved.Sealed.Should().BeFalse();
    }

    [Fact]
    public void ARingSummaryUsesThePetBlockShape()
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        CharacterSealPacketWriter.WriteSummary(packet, new CharacterSealPacketWriter.RingSummary("Velkan", 105, 70, 2500, 1));
        packet.ResetOffset();

        packet.ReadString().Should().Be("Velkan");
        packet.ReadByte().Should().Be(105);
        packet.ReadByte().Should().Be(70);
        packet.ReadUShort().Should().Be(2500);
        packet.ReadShort().Should().Be(1);
        packet.ReadByte().Should().Be(0);
    }

    [Fact]
    public void ARingOfferedInATradeCarriesItsUniqueIdAndSummary()
    {
        var ring = new CharacterSealPacketWriter.RingSummary("Velkan", 105, 70, 2500, 1, 2);
        var packet = ExchangePacketWriter.ItemOffered(ExchangePacketConstants.ExchangeOtherAdd, InventoryConstants.CypherRing, 1, 1, 11, ring);
        packet.ResetOffset();

        packet.ReadByte().Should().Be(ExchangePacketConstants.ExchangeOtherAdd);
        packet.ReadInt().Should().Be(InventoryConstants.CypherRing);
        packet.ReadInt().Should().Be(1);
        packet.ReadShort().Should().Be(1);
        packet.ReadInt().Should().Be(11);
        packet.ReadString().Should().Be("Velkan");
        packet.ReadByte().Should().Be(105);
        packet.ReadByte().Should().Be(70);
        packet.ReadUShort().Should().Be(2500);
        packet.ReadShort().Should().Be(1);
        packet.ReadByte().Should().Be(2);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void AnOrdinaryTradedItemKeepsAZeroExtension()
    {
        var packet = ExchangePacketWriter.Completed(ExchangePacketConstants.ExchangeDone, 0,
            [new ExchangePacketWriter.TransferredItem(4, Scroll, 1, 1, 11)]);
        packet.ResetOffset();

        packet.ReadByte();
        packet.ReadByte().Should().Be(ExchangePacketWriter.Succeeded);
        packet.ReadInt();
        packet.ReadUShort().Should().Be(1);
        packet.ReadByte().Should().Be(4);
        packet.ReadInt().Should().Be(Scroll);
        packet.ReadUShort();
        packet.ReadShort();
        packet.ReadByte();
        packet.ReadInt().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }
}
