using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ShoppingMallTests : GameTestBase
{
    [Fact]
    public async Task ShoppingMallPacketCoordinator_SendUnreadAsync_SendsUnreadLetterCount()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.MailBoxes.AddRange(
                    new MailBox
                    {
                        RecipientId = "Mailer",
                        SenderId = "Alpha",
                        Subject = "Unread 1",
                        Message = "Message",
                        Status = 1,
                        Type = 1,
                        SendDate = DateTime.UtcNow,
                        Deleted = false
                    },
                    new MailBox
                    {
                        RecipientId = "Mailer",
                        SenderId = "Beta",
                        Subject = "Unread 2",
                        Message = "Message",
                        Status = 1,
                        Type = 1,
                        SendDate = DateTime.UtcNow,
                        Deleted = false
                    },
                    new MailBox
                    {
                        RecipientId = "Mailer",
                        SenderId = "Gamma",
                        Subject = "Read",
                        Message = "Message",
                        Status = 2,
                        Type = 1,
                        SendDate = DateTime.UtcNow,
                        Deleted = false
                    });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 11, accountId: 22);
        session.Name = "Mailer";

        var coordinator = provider.GetRequiredService<IShoppingMallPacketCoordinator>();
        await coordinator.SendUnreadAsync(session);

        sentPackets.Should().ContainSingle();
        var sentPacket = sentPackets.Single();
        sentPacket.GetOpcode().Should().Be((byte)GameOpcodes.GS_SHOPPING_MALL);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(6);
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadByte().Should().Be(2);
    }

    [Fact]
    public async Task ShoppingMallPacketCoordinator_SendBalanceAsync_SendsTheAccountKnightCash()
    {
        using var provider = CreateProvider(_ => { });
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 11, accountId: 22);
        session.KnightCash = 250;

        var coordinator = provider.GetRequiredService<IShoppingMallPacketCoordinator>();
        await coordinator.SendBalanceAsync(session);

        sentPackets.Should().ContainSingle();
        var sentPacket = sentPackets.Single();
        sentPacket.GetOpcode().Should().Be((byte)GameOpcodes.GS_SHOPPING_MALL);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadByte().Should().Be(5);
        sentPacket.ReadInt().Should().Be(250);
    }

    [Fact]
    public async Task GamePacketHandler_GameStartPhaseTwo_SendsKnightCashBalance()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "cash-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal,
                    KnightCash = 250
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(account => account.Login == "cash-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "CashReady",
                    Race = 1,
                    Class = 101,
                    Face = 2,
                    Hair = 3,
                    Level = 10,
                    Hp = 100,
                    Mp = 100,
                    MapId = 1,
                    X = 10,
                    Z = 20,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
            });

        var packetHandler = provider.GetRequiredService<IPacketHandler>();
        var accountId = await GetAccountIdAsync(provider, "cash-user");
        var characterId = await GetCharacterIdAsync(provider, "CashReady");

        var sentPackets = new List<Packet>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.IsCryptoEnabled.Returns(true);
        client.AccountId.Returns(accountId);
        client.CharacterId.Returns(characterId);
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        (await provider.GetRequiredService<IAccountLockService>()
            .AcquireAsync(client, accountId)).Granted.Should().BeTrue();

        var request = new Packet(GameOpcodes.GS_GAMESTART);
        request.WriteByte(2);

        await packetHandler.HandlePacket(client, request);

        var balance = sentPackets.Single(packet =>
            packet.GetOpcode() == (byte)GameOpcodes.GS_SHOPPING_MALL
            && packet.GetData().Length >= 6
            && packet.GetData()[0] == 1
            && packet.GetData()[1] == 5);
        balance.ResetOffset();
        balance.ReadByte().Should().Be(1);
        balance.ReadByte().Should().Be(5);
        balance.ReadInt().Should().Be(250);
    }

    [Fact]
    public async Task ShoppingMallPacketCoordinator_HandleAsync_StoreOpenRejectsPrivateArena()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 23, accountId: 33);
        session.ZoneId = 40;
        session.Hp = 100;

        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(1);

        var coordinator = provider.GetRequiredService<IShoppingMallPacketCoordinator>();
        await coordinator.HandleAsync(client, request);

        sentPackets.Should().ContainSingle();
        var sentPacket = sentPackets.Single();
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadShort().Should().Be(-5);
    }

    [Fact]
    public async Task ShoppingMallPacketCoordinator_HandleAsync_StoreOpenSendsCatalogCategoriesAndBalance()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, 30, 40);
        session.Hp = 100;
        session.KnightCash = 250;
        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(1);

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(client, request);

        sentPackets.Should().HaveCount(4);
        foreach (var packet in sentPackets)
            packet.ResetOffset();
        sentPackets.Select(packet => packet.ReadByte()).Should().Equal(1, 1, 1, 1);
        sentPackets[1].ReadByte().Should().Be(3);
        sentPackets[2].ReadByte().Should().Be(4);
        sentPackets[3].ReadByte().Should().Be(5);
        sentPackets[3].ReadInt().Should().Be(250);
    }

    private const int BuyerId = 27;
    private const int FriendId = 28;
    private const int StrangerId = 29;
    private const int ClanmateId = 30;
    private const short BuyerClan = 5;
    private const int FirstStoreItemId = 800079000;
    private const int NonCountableItemId = 810039000;

    private static ServiceProvider StoreProvider(int entries, int price = 100, params PusDiscountData[] discounts) => CreateProvider(
        db =>
        {
            for (var id = 1; id <= entries; id++)
                db.PusItems.Add(new PusItemData { Id = id, ItemId = FirstStoreItemId + id * 1000, Price = price, Category = 1, Featured = id == 1 });
            db.PusItems.Add(new PusItemData { Id = 100, ItemId = NonCountableItemId, Price = price, Category = 2 });
            db.PusDiscounts.AddRange(discounts);
            db.Characters.AddRange(
                new Character { Id = BuyerId, AccountId = 37, Name = "Buyer", Level = 60, Class = 105 },
                new Character { Id = FriendId, AccountId = 38, Name = "Friend", Level = 72, Class = 211 },
                new Character { Id = StrangerId, AccountId = 39, Name = "Stranger", Level = 40, Class = 101 },
                new Character { Id = ClanmateId, AccountId = 40, Name = "Clanmate", Level = 55, Class = 103, KnightsId = BuyerClan });
            db.Friendships.Add(new Friendship { CharacterId = BuyerId, FriendCharacterId = FriendId, AddedAt = DateTime.UtcNow });
        },
        gameData => gameData.GetItem(Arg.Any<int>()).Returns(call => new ItemData
        {
            Num = call.Arg<int>(),
            Countable = (byte)(call.Arg<int>() == NonCountableItemId ? 0 : 1),
            Duration = 1,
        }));

    private static (UserSession Session, List<Packet> Sent) StoreBuyer(ServiceProvider provider, int knightCash)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sent.Add(packet)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, BuyerId, 37);
        session.Name = "Buyer";
        session.Hp = 100;
        session.KnightCash = knightCash;
        session.KnightsId = BuyerClan;
        return (session, sent);
    }

    private static Packet CartRequest(string recipient, params (int CatalogId, ushort Count)[] lines) =>
        PricedCartRequest(recipient, lines.Select(line => (line.CatalogId, line.Count, 100)).ToArray());

    private static Packet PricedCartRequest(string recipient, params (int CatalogId, ushort Count, int UnitPrice)[] lines)
    {
        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(ShoppingMallStoreService.StorePurchase);
        request.WriteByte(ShoppingMallStoreService.PurchaseCart);
        request.WriteSByteString(recipient);
        request.WriteByte((byte)lines.Length);
        foreach (var (catalogId, count, unitPrice) in lines)
        {
            request.WriteInt(catalogId);
            request.WriteUShort(count);
            request.WriteInt(unitPrice);
        }

        return request;
    }

    private static Packet RecipientCheckRequest(string name)
    {
        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(ShoppingMallStoreService.StorePurchase);
        request.WriteByte(ShoppingMallStoreService.PurchaseCheckRecipient);
        request.WriteSByteString(name);
        return request;
    }

    private static Packet PurchaseReply(List<Packet> sent, byte sub)
    {
        var packet = sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_SHOPPING_MALL
            && p.GetData()[0] == ShoppingMallStoreService.StorePurchase && p.GetData()[1] == sub);
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadByte();
        return packet;
    }

    private static async Task<List<Mail>> MailsAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Mails.Include(m => m.Attachments).ToListAsync();
    }

    [Fact]
    public async Task Purchase_ChargesTheCartTotalOnce_AndMailsEveryLineInOneMail()
    {
        using var provider = StoreProvider(entries: 6);
        var (buyer, sent) = StoreBuyer(provider, knightCash: 2_000);

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(buyer.Client,
            CartRequest("", (1, 2), (2, 1), (3, 1), (4, 1), (5, 1), (6, 3)));

        var reply = PurchaseReply(sent, ShoppingMallStoreService.PurchaseCart);
        reply.ReadByte().Should().Be((byte)PowerUpStoreResult.Succeeded);
        reply.ReadInt().Should().Be(1_100);
        buyer.KnightCash.Should().Be(1_100);
        buyer.Inventory.Should().OnlyContain(slot => slot.IsEmpty);

        var mail = (await MailsAsync(provider)).Should().ContainSingle().Subject;
        mail.RecipientCharacterId.Should().Be(BuyerId);
        mail.SenderName.Should().Be(MailLimits.SystemSenderName);
        mail.Kind.Should().Be(MailKind.Store);
        mail.Attachments.Select(a => (a.Kind, a.ItemId, a.Count)).Should().Equal(
            (MailAttachmentKind.Item, FirstStoreItemId + 1000, 2),
            (MailAttachmentKind.Item, FirstStoreItemId + 2000, 1),
            (MailAttachmentKind.Item, FirstStoreItemId + 3000, 1),
            (MailAttachmentKind.Item, FirstStoreItemId + 4000, 1),
            (MailAttachmentKind.Item, FirstStoreItemId + 5000, 1),
            (MailAttachmentKind.Item, FirstStoreItemId + 6000, 3));
    }

    [Fact]
    public async Task Purchase_KeepsSeveralUnitsOfANonCountableItemOnOneAttachment()
    {
        using var provider = StoreProvider(entries: 0);
        var (buyer, sent) = StoreBuyer(provider, knightCash: 1_000);

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(buyer.Client, CartRequest("", (100, 3)));

        PurchaseReply(sent, ShoppingMallStoreService.PurchaseCart).ReadByte().Should().Be((byte)PowerUpStoreResult.Succeeded);
        buyer.KnightCash.Should().Be(700);
        var attachment = (await MailsAsync(provider)).Single().Attachments.Should().ContainSingle().Subject;
        (attachment.ItemId, attachment.Count).Should().Be((NonCountableItemId, 3));
    }

    [Fact]
    public async Task Purchase_RefusesACartOverTheBalance_AndChargesNothing()
    {
        using var provider = StoreProvider(entries: 2, price: 600);
        var (buyer, sent) = StoreBuyer(provider, knightCash: 1_000);

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(buyer.Client, PricedCartRequest("", (1, 1, 600), (2, 1, 600)));

        var reply = PurchaseReply(sent, ShoppingMallStoreService.PurchaseCart);
        reply.ReadByte().Should().Be((byte)PowerUpStoreResult.NotEnoughCash);
        reply.ReadInt().Should().Be(1_000);
        buyer.KnightCash.Should().Be(1_000);
        (await MailsAsync(provider)).Should().BeEmpty();
    }

    [Fact]
    public async Task Purchase_RefusesAnUnknownCatalogEntry_AndChargesNothing()
    {
        using var provider = StoreProvider(entries: 1);
        var (buyer, sent) = StoreBuyer(provider, knightCash: 1_000);

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(buyer.Client, CartRequest("", (1, 1), (42, 1)));

        PurchaseReply(sent, ShoppingMallStoreService.PurchaseCart).ReadByte().Should().Be((byte)PowerUpStoreResult.Unavailable);
        buyer.KnightCash.Should().Be(1_000);
        (await MailsAsync(provider)).Should().BeEmpty();
    }

    [Fact]
    public async Task Purchase_RefusesAPriceThePlayerDidNotSee_AndChargesNothing()
    {
        using var provider = StoreProvider(entries: 1);
        var (buyer, sent) = StoreBuyer(provider, knightCash: 1_000);

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(buyer.Client, PricedCartRequest("", (1, 1, 75)));

        PurchaseReply(sent, ShoppingMallStoreService.PurchaseCart).ReadByte().Should().Be((byte)PowerUpStoreResult.PriceChanged);
        buyer.KnightCash.Should().Be(1_000);
        (await MailsAsync(provider)).Should().BeEmpty();
    }

    [Fact]
    public async Task Purchase_ChargesTheActiveDiscount()
    {
        var discount = new PusDiscountData { Id = 1, PusItemId = 1, Price = 75, StartsAt = DateTime.UtcNow.AddHours(-1), Duration = 5 };
        using var provider = StoreProvider(1, 100, discount);
        var (buyer, sent) = StoreBuyer(provider, knightCash: 1_000);

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(buyer.Client, PricedCartRequest("", (1, 2, 75)));

        PurchaseReply(sent, ShoppingMallStoreService.PurchaseCart).ReadByte().Should().Be((byte)PowerUpStoreResult.Succeeded);
        buyer.KnightCash.Should().Be(850);
    }

    [Fact]
    public async Task StoreOpen_TheCatalogueCarriesFeaturedAndTheActiveDiscount_AndLeavesOutUnknownItems()
    {
        var discount = new PusDiscountData { Id = 1, PusItemId = 1, Price = 75, StartsAt = DateTime.UtcNow.AddHours(-1), Duration = 5 };
        using var provider = CreateProvider(
            db =>
            {
                db.PusItems.AddRange(
                    new PusItemData { Id = 1, ItemId = FirstStoreItemId, Price = 100, Category = 1, Featured = true },
                    new PusItemData { Id = 2, ItemId = NonCountableItemId, Price = 100, Category = 1 });
                db.PusDiscounts.Add(discount);
            },
            gameData => gameData.GetItem(FirstStoreItemId).Returns(new ItemData { Num = FirstStoreItemId, Countable = 1 }));
        var (buyer, sent) = StoreBuyer(provider, knightCash: 0);
        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(1);

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(buyer.Client, request);

        var catalogue = sent[1];
        catalogue.ResetOffset();
        catalogue.ReadByte().Should().Be(1);
        catalogue.ReadByte().Should().Be(3);
        catalogue.ReadUShort().Should().Be(1);
        catalogue.ReadInt().Should().Be(1);
        catalogue.ReadInt().Should().Be(FirstStoreItemId);
        catalogue.ReadByte().Should().Be(1);
        catalogue.ReadInt().Should().Be(100);
        catalogue.ReadByte().Should().Be(1);
        catalogue.ReadInt().Should().Be(75);
        catalogue.ReadLong().Should().Be(new DateTimeOffset(discount.EndsAt!.Value, TimeSpan.Zero).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Purchase_AsAGift_MailsTheRecipient_AndChargesTheBuyer()
    {
        using var provider = StoreProvider(entries: 1);
        var (buyer, sent) = StoreBuyer(provider, knightCash: 1_000);

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(buyer.Client, CartRequest("Friend", (1, 2)));

        PurchaseReply(sent, ShoppingMallStoreService.PurchaseCart).ReadByte().Should().Be((byte)PowerUpStoreResult.Succeeded);
        buyer.KnightCash.Should().Be(800);
        var mail = (await MailsAsync(provider)).Should().ContainSingle().Subject;
        mail.RecipientCharacterId.Should().Be(FriendId);
        mail.Subject.Should().Contain("Buyer");
        mail.Attachments.Single().Count.Should().Be(2);
    }

    [Fact]
    public async Task Purchase_AsAGift_RefusesAnUnknownRecipient_AndYourself()
    {
        using var provider = StoreProvider(entries: 1);
        var (buyer, sent) = StoreBuyer(provider, knightCash: 1_000);
        var coordinator = provider.GetRequiredService<IShoppingMallPacketCoordinator>();

        await coordinator.HandleAsync(buyer.Client, CartRequest("Nobody", (1, 1)));
        PurchaseReply(sent, ShoppingMallStoreService.PurchaseCart).ReadByte().Should().Be((byte)PowerUpStoreResult.RecipientNotFound);

        await coordinator.HandleAsync(buyer.Client, CartRequest("Buyer", (1, 1)));
        PurchaseReply(sent, ShoppingMallStoreService.PurchaseCart).ReadByte().Should().Be((byte)PowerUpStoreResult.RecipientIsSelf);

        buyer.KnightCash.Should().Be(1_000);
        (await MailsAsync(provider)).Should().BeEmpty();
    }

    [Fact]
    public async Task Purchase_WhenTheMailCannotBeWritten_RefundsAndReportsAFailure()
    {
        using var provider = StoreProvider(entries: 1);
        var (buyer, sent) = StoreBuyer(provider, knightCash: 1_000);
        var mail = Substitute.For<IMailService>();
        mail.SendSystemMailAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<MailAttachmentDraft>>(), Arg.Any<MailKind>())
            .Returns(Task.FromException(new InvalidOperationException("database down")));
        var store = new ShoppingMallStoreService(
            provider.GetRequiredService<ICharacterStatePersister>(),
            provider.GetRequiredService<IGameDataService>(),
            mail,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ShoppingMallStoreService>.Instance);
        var request = CartRequest("", (1, 1));
        request.ReadByte();

        await store.HandlePurchaseAsync(buyer, request);

        var reply = PurchaseReply(sent, ShoppingMallStoreService.PurchaseCart);
        reply.ReadByte().Should().Be((byte)PowerUpStoreResult.Failed);
        reply.ReadInt().Should().Be(1_000);
        buyer.KnightCash.Should().Be(1_000);
    }

    [Fact]
    public async Task CheckRecipient_ReturnsTheCharactersNameLevelAndClass()
    {
        using var provider = StoreProvider(entries: 0);
        var (buyer, sent) = StoreBuyer(provider, knightCash: 0);

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(buyer.Client, RecipientCheckRequest("Friend"));

        var reply = PurchaseReply(sent, ShoppingMallStoreService.PurchaseCheckRecipient);
        reply.ReadByte().Should().Be((byte)PowerUpStoreResult.Succeeded);
        reply.ReadSByteString().Should().Be("Friend");
        reply.ReadByte().Should().Be(72);
        reply.ReadUShort().Should().Be(211);
    }

    [Fact]
    public async Task Purchase_AsAGift_RefusesSomeoneWhoIsNeitherAFriendNorInTheClan()
    {
        using var provider = StoreProvider(entries: 1);
        var (buyer, sent) = StoreBuyer(provider, knightCash: 1_000);

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(buyer.Client, CartRequest("Stranger", (1, 1)));

        PurchaseReply(sent, ShoppingMallStoreService.PurchaseCart).ReadByte().Should().Be((byte)PowerUpStoreResult.RecipientNotAllowed);
        buyer.KnightCash.Should().Be(1_000);
        (await MailsAsync(provider)).Should().BeEmpty();
    }

    [Fact]
    public async Task CheckRecipient_AcceptsAClanMate_AndRefusesAStranger()
    {
        using var provider = StoreProvider(entries: 0);
        var (buyer, sent) = StoreBuyer(provider, knightCash: 0);
        var coordinator = provider.GetRequiredService<IShoppingMallPacketCoordinator>();

        await coordinator.HandleAsync(buyer.Client, RecipientCheckRequest("Clanmate"));
        PurchaseReply(sent, ShoppingMallStoreService.PurchaseCheckRecipient).ReadByte().Should().Be((byte)PowerUpStoreResult.Succeeded);

        await coordinator.HandleAsync(buyer.Client, RecipientCheckRequest("Stranger"));
        PurchaseReply(sent, ShoppingMallStoreService.PurchaseCheckRecipient).ReadByte().Should().Be((byte)PowerUpStoreResult.RecipientNotAllowed);
    }

    [Fact]
    public async Task CheckRecipient_RefusesAnUnknownName_AndYourself()
    {
        using var provider = StoreProvider(entries: 0);
        var (buyer, sent) = StoreBuyer(provider, knightCash: 0);
        var coordinator = provider.GetRequiredService<IShoppingMallPacketCoordinator>();

        await coordinator.HandleAsync(buyer.Client, RecipientCheckRequest("Nobody"));
        PurchaseReply(sent, ShoppingMallStoreService.PurchaseCheckRecipient).ReadByte().Should().Be((byte)PowerUpStoreResult.RecipientNotFound);

        await coordinator.HandleAsync(buyer.Client, RecipientCheckRequest("Buyer"));
        PurchaseReply(sent, ShoppingMallStoreService.PurchaseCheckRecipient).ReadByte().Should().Be((byte)PowerUpStoreResult.RecipientIsSelf);
    }

    [Fact]
    public async Task StoreOpen_WithAFullBag_StillOpens()
    {
        using var provider = StoreProvider(entries: 1);
        var (buyer, sent) = StoreBuyer(provider, knightCash: 0);
        for (var i = InventoryConstants.SlotMax; i < InventoryConstants.InventoryTotal; i++)
        {
            buyer.Inventory[i].ItemId = NonCountableItemId;
            buyer.Inventory[i].Count = 1;
        }

        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(1);
        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(buyer.Client, request);

        var opened = sent.First();
        opened.ResetOffset();
        opened.ReadByte().Should().Be(1);
        opened.ReadShort().Should().Be(1);
    }

    [Fact]
    public async Task ShoppingMallPacketCoordinator_HandleAsync_LetterSendUsesInventoryRelativeSlot()
    {
        const int itemId = 389070000;

        using var provider = CreateProvider(
            db =>
            {
                db.Characters.Add(new Character
                {
                    AccountId = 1,
                    Slot = 0,
                    Name = "Recipient",
                    Race = 1,
                    Class = 101,
                    Face = 1,
                    Hair = 1,
                    Level = 10,
                    Hp = 100,
                    Mp = 100,
                    MapId = 1,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
            },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Race = 1
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 24, accountId: 34);
        session.Name = "Sender";
        session.Money = 20000;
        session.Hp = 100;
        session.Inventory[InventoryConstants.RightHand].ItemId = itemId;
        session.Inventory[InventoryConstants.RightHand].Durability = 100;
        session.Inventory[InventoryConstants.RightHand].Count = 1;

        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(6);
        request.WriteByte(6);
        request.WriteSByteString("Recipient");
        request.WriteSByteString("Subject");
        request.WriteByte(2);
        request.WriteInt(itemId);
        request.WriteByte(0);
        request.WriteInt(0);
        request.WriteString("Message");

        var coordinator = provider.GetRequiredService<IShoppingMallPacketCoordinator>();
        await coordinator.HandleAsync(client, request);

        var sentPacket = sentPackets.Last(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_SHOPPING_MALL);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(6);
        sentPacket.ReadByte().Should().Be(6);
        sentPacket.ReadByte().Should().Be(unchecked((byte)-1));

        session.Inventory[InventoryConstants.RightHand].ItemId.Should().Be(itemId);
        session.Inventory[InventoryConstants.RightHand].Count.Should().Be(1);
        session.Money.Should().Be(20000);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.MailBoxes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ALetterNamingAnotherItemThanTheSlotHoldsSendsNothing()
    {
        const int named = 389050000;
        const int held = 389060000;
        const byte tradeLockedRace = 70;
        const int purse = 20_000;

        using var provider = CreateProvider(
            db => db.Characters.AddRange(
                new Character
                {
                    AccountId = 1, Slot = 0, Name = "Recipient", Race = 1, Class = 101, Level = 10, MapId = 1,
                    Items = new byte[InventoryConstants.InventoryTotal * 8], SkillPointData = new byte[9],
                },
                new Character
                {
                    Id = 26, AccountId = 36, Slot = 0, Name = "Sender", Race = 1, Class = 101, Level = 10, MapId = 1,
                    Items = new byte[InventoryConstants.InventoryTotal * 8], SkillPointData = new byte[9],
                }),
            gameData =>
            {
                gameData.GetItem(named).Returns(new ItemData { Num = named, Race = 1 });
                gameData.GetItem(held).Returns(new ItemData { Num = held, Race = tradeLockedRace });
            });
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId: 26, accountId: 36);
        session.Name = "Sender";
        session.Money = purse;
        session.Hp = session.MaxHp = 100;
        var slot = session.Inventory[InventoryConstants.InventoryStart];
        (slot.ItemId, slot.Count, slot.Durability) = (held, 1, 100);
        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(ShoppingMallLetterProtocol.StoreLetter);
        request.WriteByte(ShoppingMallLetterProtocol.LetterSend);
        request.WriteSByteString("Recipient");
        request.WriteSByteString("Subject");
        request.WriteByte(ShoppingMallPacketWriter.LetterTypeWithItem);
        request.WriteInt(named);
        request.WriteByte(0);
        request.WriteInt(0);
        request.WriteString("Message");

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(client, request);

        slot.ItemId.Should().Be(held, "the slot holds another item than the one named");
        session.Money.Should().Be(purse);
        await using var scope = provider.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().MailBoxes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ShoppingMallPacketCoordinator_HandleAsync_LetterDeleteOverflowReturnsError()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 25, accountId: 35);
        session.Name = "Mailer";

        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(6);
        request.WriteByte(7);
        request.WriteByte(6);
        for (var i = 0; i < 6; i++)
            request.WriteInt(1000 + i);

        var coordinator = provider.GetRequiredService<IShoppingMallPacketCoordinator>();
        await coordinator.HandleAsync(client, request);

        sentPackets.Should().ContainSingle();
        var sentPacket = sentPackets.Single();
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(6);
        sentPacket.ReadByte().Should().Be(7);
        sentPacket.ReadByte().Should().Be(unchecked((byte)-3));
    }

    [Fact]
    public async Task ShoppingMallPacketCoordinator_HandleAsync_LetterSendIgnoresDisabledCoinsField()
    {
        const int itemId = 810002000;

        using var provider = CreateProvider(
            db =>
            {
                db.Characters.Add(new Character
                {
                    AccountId = 1,
                    Slot = 0,
                    Name = "Recipient",
                    Race = 1,
                    Class = 101,
                    Face = 1,
                    Hair = 1,
                    Level = 10,
                    Hp = 100,
                    Mp = 100,
                    MapId = 1,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
                db.Characters.Add(new Character { Id = 26, AccountId = 36, Name = "Sender" });
            },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Race = 1
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 26, accountId: 36);
        session.Hp = 100;
        session.Name = "Sender";
        session.Money = 20000;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = itemId;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 25;
        session.Inventory[InventoryConstants.InventoryStart].Count = 1;

        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(6);
        request.WriteByte(6);
        request.WriteSByteString("Recipient");
        request.WriteSByteString("Subject");
        request.WriteByte(2);
        request.WriteInt(itemId);
        request.WriteByte(0);
        request.WriteInt(5000);
        request.WriteString("Message");

        var coordinator = provider.GetRequiredService<IShoppingMallPacketCoordinator>();
        await coordinator.HandleAsync(client, request);

        var response = sentPackets.Last(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_SHOPPING_MALL);
        response.ResetOffset();
        response.ReadByte().Should().Be(6);
        response.ReadByte().Should().Be(6);
        response.ReadByte().Should().Be(1);

        session.Money.Should().Be(10000);
        session.Inventory[InventoryConstants.InventoryStart].IsEmpty.Should().BeTrue();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var letter = await db.MailBoxes.SingleAsync();
        letter.Coins.Should().Be(0);
        letter.ItemId.Should().Be(itemId);
    }

    [Fact]
    public async Task ShoppingMallPacketCoordinator_HandleAsync_LetterSendFromTheBackHalfOfTheBagNamesThatSlot()
    {
        const int itemId = 810002000;
        const byte backHalfSlot = 20;

        using var provider = CreateProvider(
            db =>
            {
                db.Characters.Add(new Character
                {
                    AccountId = 1,
                    Slot = 0,
                    Name = "Recipient",
                    Race = 1,
                    Class = 101,
                    Face = 1,
                    Hair = 1,
                    Level = 10,
                    Hp = 100,
                    Mp = 100,
                    MapId = 1,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
                db.Characters.Add(new Character { Id = 27, AccountId = 37, Name = "Sender" });
            },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Race = 1
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 27, accountId: 37);
        session.Hp = 100;
        session.Name = "Sender";
        session.Money = 20000;
        session.Inventory[InventoryConstants.InventoryStart + backHalfSlot].ItemId = itemId;
        session.Inventory[InventoryConstants.InventoryStart + backHalfSlot].Durability = 25;
        session.Inventory[InventoryConstants.InventoryStart + backHalfSlot].Count = 1;

        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(6);
        request.WriteByte(6);
        request.WriteSByteString("Recipient");
        request.WriteSByteString("Subject");
        request.WriteByte(2);
        request.WriteInt(itemId);
        request.WriteByte(backHalfSlot);
        request.WriteInt(0);
        request.WriteString("Message");

        var coordinator = provider.GetRequiredService<IShoppingMallPacketCoordinator>();
        await coordinator.HandleAsync(client, request);

        session.Inventory[InventoryConstants.InventoryStart + backHalfSlot].IsEmpty.Should().BeTrue();
        CountChangePackets.Positions(sentPackets).Should().Equal(backHalfSlot);
    }

    [Fact]
    public async Task ShoppingMallPacketCoordinator_HandleAsync_LetterGetItemStacksExistingItemUsingRelativeSlot()
    {
        const int itemId = 810003000;
        const int letterId = 1001;

        using var provider = CreateProvider(
            db =>
            {
                db.Characters.Add(new Character { Id = 27, AccountId = 37, Name = "Mailer" });
                db.MailBoxes.Add(new MailBox
                {
                    LetterId = letterId,
                    RecipientId = "Mailer",
                    SenderId = "Sender",
                    Subject = "Gift",
                    Message = "Take it",
                    Type = 2,
                    Status = 1,
                    ItemId = itemId,
                    Count = 2,
                    Durability = 10,
                    Coins = 0,
                    SendDate = DateTime.UtcNow,
                    Deleted = false
                });
            },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Countable = 1,
                    Weight = 1,
                    Duration = 10
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 27, accountId: 37);
        session.Name = "Mailer";
        session.Hp = 100;
        session.Stats.MaxWeight = 100;
        session.Stats.ItemWeight = 5;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = itemId;
        session.Inventory[InventoryConstants.InventoryStart].Count = 3;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 5;

        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(6);
        request.WriteByte(4);
        request.WriteInt(letterId);

        var coordinator = provider.GetRequiredService<IShoppingMallPacketCoordinator>();
        await coordinator.HandleAsync(client, request);

        session.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(5);
        session.Inventory[InventoryConstants.InventoryStart].Durability.Should().Be(15);

        var stackPacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_COUNT_CHANGE);
        stackPacket.ResetOffset();
        stackPacket.ReadShort().Should().Be(1);
        stackPacket.ReadByte().Should().Be(1);
        stackPacket.ReadByte().Should().Be(0);
        stackPacket.ReadInt().Should().Be(itemId);
        stackPacket.ReadInt().Should().Be(5);
        stackPacket.ReadByte().Should().Be(0);
        stackPacket.ReadShort().Should().Be(15);

        var response = sentPackets.Last(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_SHOPPING_MALL);
        response.ResetOffset();
        response.ReadByte().Should().Be(6);
        response.ReadByte().Should().Be(4);
        response.ReadByte().Should().Be(1);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.MailBoxes.SingleAsync(mail => mail.LetterId == letterId)).Status.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASentLetterStoresTheItemsStateAndSerialButTakesNoTimedItem(bool timed)
    {
        const long rentalEnds = 4_102_444_800;
        const int serial = 4242;

        using var provider = CreateProvider(
            db => db.Characters.AddRange(
                new Character
                {
                    AccountId = 1, Slot = 0, Name = "Recipient", Race = 1, Class = 101, Face = 1, Hair = 1, Level = 10,
                    Hp = 100, Mp = 100, MapId = 1, Items = new byte[InventoryConstants.InventoryTotal * 8], SkillPointData = new byte[9],
                },
                new Character { Id = 29, AccountId = 39, Name = "Sender" }),
            gameData => gameData.GetItem(InventoryConstants.CypherRing).Returns(new ItemData
            {
                Num = InventoryConstants.CypherRing, Duration = 1,
            }));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId: 29, accountId: 39);
        session.Hp = 100;
        session.Name = "Sender";
        session.Money = 20000;
        var ring = session.Inventory[InventoryConstants.InventoryStart];
        ring.ItemId = InventoryConstants.CypherRing;
        ring.Count = 1;
        ring.Durability = 1;
        ring.Flag = (byte)ItemFlag.NotBound;
        ring.ExpiresAt = timed ? rentalEnds : 0;
        ring.UniqueId = serial;

        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(6);
        request.WriteByte(6);
        request.WriteSByteString("Recipient");
        request.WriteSByteString("Subject");
        request.WriteByte(2);
        request.WriteInt(InventoryConstants.CypherRing);
        request.WriteByte(0);
        request.WriteInt(0);
        request.WriteString("Message");
        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(client, request);

        await using var scope = provider.CreateAsyncScope();
        var letters = await scope.ServiceProvider.GetRequiredService<AppDbContext>().MailBoxes.ToListAsync();
        if (timed)
        {
            letters.Should().BeEmpty("a timed item cannot change hands");
            ring.ItemId.Should().Be(InventoryConstants.CypherRing);
            return;
        }

        var letter = letters.Single();
        letter.SerialNumber.Should().Be(serial, "the letter must not drop the sealed character the ring holds");
        letter.Flag.Should().Be((byte)ItemFlag.NotBound);
    }

    [Fact]
    public async Task ALetterItemArrivesWithItsStateExpiryAndSerialAndJoinsNoOtherStack()
    {
        const int itemId = 810003000;
        const int letterId = 1002;
        const long rentalEnds = 4_102_444_800;
        const int serial = 4242;

        using var provider = CreateProvider(
            db =>
            {
                db.Characters.Add(new Character { Id = 28, AccountId = 38, Name = "Mailer" });
                db.MailBoxes.Add(new MailBox
                {
                    LetterId = letterId, RecipientId = "Mailer", SenderId = "Sender", Subject = "Gift", Message = "Take it",
                    Type = 2, Status = 1, ItemId = itemId, Count = 2, Durability = 10, Flag = (byte)ItemFlag.NotBound,
                    ExpiresAt = rentalEnds, SerialNumber = serial, Coins = 0, SendDate = DateTime.UtcNow, Deleted = false,
                });
            },
            gameData => gameData.GetItem(itemId).Returns(new ItemData { Num = itemId, Countable = 1, Weight = 1, Duration = 10 }));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId: 28, accountId: 38);
        session.Name = "Mailer";
        session.Hp = 100;
        session.Stats.MaxWeight = 100;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = itemId;
        session.Inventory[InventoryConstants.InventoryStart].Count = 3;

        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(6);
        request.WriteByte(4);
        request.WriteInt(letterId);
        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(client, request);

        session.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(3);
        var arrived = session.Inventory.Single(slot => slot.ItemId == itemId && slot.UniqueId == serial);
        arrived.Count.Should().Be(2);
        arrived.Flag.Should().Be((byte)ItemFlag.NotBound);
        arrived.ExpiresAt.Should().Be(rentalEnds);
    }

    [Fact]
    public async Task ShoppingMallPacketCoordinator_HandleAsync_LetterGetItemIsRefusedWhileTrading()
    {
        const int itemId = 810003000;
        const int letterId = 1003;

        using var provider = CreateProvider(
            db => db.MailBoxes.Add(new MailBox
            {
                LetterId = letterId, RecipientId = "Mailer", SenderId = "Sender", Subject = "Gift", Message = "Take it",
                Type = 2, Status = 1, ItemId = itemId, Count = 2, Durability = 10, Coins = 0,
                SendDate = DateTime.UtcNow, Deleted = false,
            }),
            gameData => gameData.GetItem(itemId).Returns(new ItemData { Num = itemId, Countable = 1, Weight = 1, Duration = 10 }));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId: 28, accountId: 38);
        session.Name = "Mailer";
        session.Hp = 100;
        session.Stats.MaxWeight = 100;
        session.Trade.ExchangeUser = 9_999;

        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(6);
        request.WriteByte(4);
        request.WriteInt(letterId);
        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(client, request);

        session.Inventory.Should().NotContain(slot => slot.ItemId == itemId, "a trading player's bag takes nothing in");
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.MailBoxes.SingleAsync(mail => mail.LetterId == letterId)).Status.Should().Be(1);
    }

    [Fact]
    public async Task ShoppingMallPacketCoordinator_HandleAsync_LetterGetItemWhoseCoinsDoNotFitWaits()
    {
        const int itemId = 810003000;
        const int letterId = 1004;

        using var provider = CreateProvider(
            db => db.MailBoxes.Add(new MailBox
            {
                LetterId = letterId, RecipientId = "Mailer", SenderId = "System", Subject = "Gift", Message = "Take it",
                Type = 2, Status = 1, ItemId = itemId, Count = 2, Durability = 10, Coins = 1_000,
                SendDate = DateTime.UtcNow, Deleted = false,
            }),
            gameData => gameData.GetItem(itemId).Returns(new ItemData { Num = itemId, Countable = 1, Weight = 1, Duration = 10 }));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId: 29, accountId: 39);
        session.Name = "Mailer";
        session.Hp = 100;
        session.Stats.MaxWeight = 100;
        session.Money = ExchangePacketConstants.CoinMax - 100;

        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(6);
        request.WriteByte(4);
        request.WriteInt(letterId);
        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(client, request);

        session.Money.Should().Be(ExchangePacketConstants.CoinMax - 100, "coins past the cap are not taken in");
        session.Inventory.Should().NotContain(slot => slot.ItemId == itemId, "the letter is claimed whole or not at all");
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.MailBoxes.SingleAsync(mail => mail.LetterId == letterId)).Status.Should().Be(1);
    }

    [Fact]
    public async Task ShoppingMallPacketCoordinator_HandleAsync_LetterGetItemRejectsOverweightReceive()
    {
        const int itemId = 810004000;
        const int letterId = 1002;

        using var provider = CreateProvider(
            db =>
            {
                db.MailBoxes.Add(new MailBox
                {
                    LetterId = letterId,
                    RecipientId = "Mailer",
                    SenderId = "Sender",
                    Subject = "Heavy",
                    Message = "Too heavy",
                    Type = 2,
                    Status = 1,
                    ItemId = itemId,
                    Count = 1,
                    Durability = 10,
                    Coins = 0,
                    SendDate = DateTime.UtcNow,
                    Deleted = false
                });
            },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Countable = 0,
                    Weight = 50,
                    Duration = 10
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 28, accountId: 38);
        session.Name = "Mailer";
        session.Hp = 100;
        session.Stats.MaxWeight = 10;
        session.Stats.ItemWeight = 0;

        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(6);
        request.WriteByte(4);
        request.WriteInt(letterId);

        var coordinator = provider.GetRequiredService<IShoppingMallPacketCoordinator>();
        await coordinator.HandleAsync(client, request);

        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.ReadByte().Should().Be(6);
        sentPacket.ReadByte().Should().Be(4);
        sentPacket.ReadByte().Should().Be(unchecked((byte)-1));

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.MailBoxes.SingleAsync(mail => mail.LetterId == letterId)).Status.Should().Be(1);
    }

    [Fact]
    public async Task ALetterToASealedCharacterIsRefusedAndTheRingStays()
    {
        const int sealedId = 77;
        using var provider = CreateProvider(
            db => db.Characters.AddRange(
                new Character { Id = sealedId, AccountId = 1, Name = "Sealed", Sealed = true },
                new Character { Id = 30, AccountId = 40, Name = "Sender" }),
            gameData => gameData.GetItem(InventoryConstants.CypherRing).Returns(new ItemData
            {
                Num = InventoryConstants.CypherRing, Duration = 1,
            }));
        var (session, client, sent) = LetterWriter(provider, characterId: 30, accountId: 40);
        var ring = session.Inventory[InventoryConstants.InventoryStart];
        ring.ItemId = InventoryConstants.CypherRing;
        ring.Count = 1;
        ring.UniqueId = sealedId;

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>()
            .HandleAsync(client, LetterWithItem("Sealed", InventoryConstants.CypherRing, 0));

        LetterResult(sent).Should().Be(unchecked((byte)-1));
        ring.UniqueId.Should().Be(sealedId, "a sealed character could never log in to take back the ring that unseals it");
        session.Money.Should().Be(20000);
        await using var scope = provider.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().MailBoxes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ALetterToADeletedCharacterIsRefused()
    {
        const int itemId = 810002000;
        using var provider = CreateProvider(
            db => db.Characters.AddRange(
                new Character { AccountId = 1, Name = "Gone", DeletionTime = DateTime.UtcNow },
                new Character { Id = 33, AccountId = 43, Name = "Sender" }),
            gameData => gameData.GetItem(itemId).Returns(new ItemData { Num = itemId, Race = 1 }));
        var (session, client, sent) = LetterWriter(provider, characterId: 33, accountId: 43);
        var item = session.Inventory[InventoryConstants.InventoryStart];
        item.ItemId = itemId;
        item.Count = 1;

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>()
            .HandleAsync(client, LetterWithItem("Gone", itemId, 0));

        LetterResult(sent).Should().Be(unchecked((byte)-1));
        item.ItemId.Should().Be(itemId, "no one will ever read it");
        session.Money.Should().Be(20000);
    }

    [Fact]
    public async Task ALetterWhoseSaveFailsKeepsTheItemAndTheGold()
    {
        const int itemId = 810002000;
        using var provider = CreateProvider(
            db => db.Characters.AddRange(
                new Character { AccountId = 1, Name = "Recipient" },
                new Character { Id = 31, AccountId = 41, Name = "Sender" }),
            gameData => gameData.GetItem(itemId).Returns(new ItemData { Num = itemId, Race = 1 }),
            configureServices: services => services.ConfigureDbContext<AppDbContext>(
                options => options.AddInterceptors(new LetterSaveFaults())));
        var (session, client, sent) = LetterWriter(provider, characterId: 31, accountId: 41);
        var item = session.Inventory[InventoryConstants.InventoryStart];
        item.ItemId = itemId;
        item.Count = 1;
        item.Durability = 25;

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>()
            .HandleAsync(client, LetterWithItem("Recipient", itemId, 0));

        LetterResult(sent).Should().Be(unchecked((byte)-1));
        item.ItemId.Should().Be(itemId, "the letter and the bag are saved together or not at all");
        item.Durability.Should().Be(25);
        session.Money.Should().Be(20000);
    }

    [Fact]
    public async Task ALetterWhoseSaveFailsSendsWhatNoLongerFitsToTheWarehouse()
    {
        const int itemId = 810002000;
        var faults = new LetterSaveFaults();
        using var provider = CreateProvider(
            db => db.Characters.AddRange(
                new Character { AccountId = 1, Name = "Recipient" },
                new Character { Id = 34, AccountId = 44, Name = "Sender" }),
            gameData => gameData.GetItem(itemId).Returns(new ItemData { Num = itemId, Race = 1 }),
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var (session, client, _) = LetterWriter(provider, characterId: 34, accountId: 44);
        var item = session.Inventory[InventoryConstants.InventoryStart];
        item.ItemId = itemId;
        item.Count = 1;
        item.Durability = 25;
        faults.Meanwhile = () =>
        {
            for (var slot = 0; slot < InventoryConstants.HaveMax; slot++)
            {
                session.Inventory[InventoryConstants.InventoryStart + slot].ItemId = InventoryConstants.CypherRing;
                session.Inventory[InventoryConstants.InventoryStart + slot].Count = 1;
            }
            session.Money = ExchangePacketConstants.CoinMax;
        };

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>()
            .HandleAsync(client, LetterWithItem("Recipient", itemId, 0));

        session.Warehouse.Should().Contain(slot => slot.ItemId == itemId && slot.Durability == 25,
            "the bag filled while the save ran, and the item must not vanish");
        session.WarehouseMoney.Should().Be(ShoppingMallLetterProtocol.LetterSendItemCost);
    }

    [Fact]
    public async Task ALetterWhoseSaveFailsPutsTheItemBackInItsOwnSlot()
    {
        const int itemId = 810002000;
        const byte ownSlot = 5;
        using var provider = CreateProvider(
            db => db.Characters.AddRange(
                new Character { AccountId = 1, Name = "Recipient" },
                new Character { Id = 35, AccountId = 45, Name = "Sender" }),
            gameData => gameData.GetItem(itemId).Returns(new ItemData { Num = itemId, Race = 1 }),
            configureServices: services => services.ConfigureDbContext<AppDbContext>(
                options => options.AddInterceptors(new LetterSaveFaults())));
        var (session, client, _) = LetterWriter(provider, characterId: 35, accountId: 45);
        var item = session.Inventory[InventoryConstants.InventoryStart + ownSlot];
        item.ItemId = itemId;
        item.Count = 1;

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>()
            .HandleAsync(client, LetterWithItem("Recipient", itemId, ownSlot));

        item.ItemId.Should().Be(itemId, "the client puts the item back where it was");
    }

    [Fact]
    public async Task AnItemOfTheUnmailableRaceIsNotSentByLetter()
    {
        const int itemId = 810003000;
        const byte unmailableRace = 7;
        using var provider = CreateProvider(
            db => db.Characters.AddRange(
                new Character { AccountId = 1, Name = "Recipient" },
                new Character { Id = 36, AccountId = 46, Name = "Sender" }),
            gameData => gameData.GetItem(itemId).Returns(new ItemData { Num = itemId, Race = unmailableRace }));
        var (session, client, sent) = LetterWriter(provider, characterId: 36, accountId: 46);
        var item = session.Inventory[InventoryConstants.InventoryStart];
        item.ItemId = itemId;
        item.Count = 1;

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>()
            .HandleAsync(client, LetterWithItem("Recipient", itemId, 0));

        LetterResult(sent).Should().Be(unchecked((byte)-32));
        item.ItemId.Should().Be(itemId);
    }

    [Fact]
    public async Task ALetterWrittenButReportedAsFailedIsNotGivenBack()
    {
        const int itemId = 810002000;
        var faults = new FailsAfterWriting(context => context.ChangeTracker.Entries<MailBox>().Any(entry => entry.State == EntityState.Added));
        using var provider = CreateProvider(
            db => db.Characters.AddRange(
                new Character { AccountId = 1, Name = "Recipient" },
                new Character { Id = 37, AccountId = 47, Name = "Sender" }),
            gameData => gameData.GetItem(itemId).Returns(new ItemData { Num = itemId, Race = 1 }),
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var (session, client, sent) = LetterWriter(provider, characterId: 37, accountId: 47);
        var item = session.Inventory[InventoryConstants.InventoryStart];
        item.ItemId = itemId;
        item.Count = 1;
        faults.Armed = true;

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>()
            .HandleAsync(client, LetterWithItem("Recipient", itemId, 0));

        item.IsEmpty.Should().BeTrue("the letter was written, so giving the item back would duplicate it");
        session.Money.Should().Be(20000 - ShoppingMallLetterProtocol.LetterSendItemCost);
        LetterResult(sent).Should().Be(1);
        await using var scope = provider.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().MailBoxes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ALetterStillHoldingItsItemIsNotDeleted()
    {
        using var provider = CreateProvider(db => db.MailBoxes.AddRange(
            new MailBox { RecipientId = "Mailer", Type = 2, Status = 1, ItemId = InventoryConstants.CypherRing, Count = 1 },
            new MailBox { RecipientId = "Mailer", Type = 2, Status = 2, ItemId = InventoryConstants.CypherRing, Count = 1 },
            new MailBox { RecipientId = "Mailer", Type = 1, Status = 1 }));
        var (session, client, _) = LetterWriter(provider, characterId: 32, accountId: 42);
        session.Name = "Mailer";
        int[] ids;
        await using (var scope = provider.CreateAsyncScope())
            ids = await scope.ServiceProvider.GetRequiredService<AppDbContext>().MailBoxes.OrderBy(mail => mail.LetterId).Select(mail => mail.LetterId).ToArrayAsync();
        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(6);
        request.WriteByte(7);
        request.WriteByte((byte)ids.Length);
        foreach (var id in ids)
            request.WriteInt(id);

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(client, request);

        await using var check = provider.CreateAsyncScope();
        var kept = await check.ServiceProvider.GetRequiredService<AppDbContext>().MailBoxes.Where(mail => !mail.Deleted).ToListAsync();
        kept.Should().ContainSingle("only the letter whose ring was never taken stays")
            .Which.LetterId.Should().Be(ids[0]);
    }

    private static (UserSession Session, IClient Client, List<Packet> Sent) LetterWriter(ServiceProvider provider, int characterId, int accountId)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sent.Add(packet)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId, accountId);
        session.Hp = 100;
        session.Name = "Sender";
        session.Money = 20000;
        session.Stats = new DerivedStats { MaxWeight = 10_000 };
        return (session, client, sent);
    }

    private static Packet LetterWithItem(string recipient, int itemId, byte bagPosition)
    {
        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(6);
        request.WriteByte(6);
        request.WriteSByteString(recipient);
        request.WriteSByteString("Subject");
        request.WriteByte(2);
        request.WriteInt(itemId);
        request.WriteByte(bagPosition);
        request.WriteInt(0);
        request.WriteString("Message");
        return request;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ALetterClaimIsSavedWithTheBag(bool saveFails)
    {
        const int itemId = 810003000;
        const int letterId = 1002;
        const int coins = 500;
        const int purse = 1_000;
        var faults = new ClaimFaults { Armed = saveFails };
        using var provider = CreateProvider(
            db =>
            {
                db.Characters.Add(new Character { Id = 29, AccountId = 39, Name = "Claimer", Money = purse });
                db.MailBoxes.Add(new MailBox
                {
                    LetterId = letterId, RecipientId = "Claimer", SenderId = "Sender", Subject = "Gift", Message = "Take it",
                    Type = ShoppingMallPacketWriter.LetterTypeWithItem, Status = ShoppingMallLetterProtocol.LetterStatusNew,
                    ItemId = itemId, Count = 2, Durability = 10, Coins = coins, SendDate = DateTime.UtcNow,
                });
            },
            gameData => gameData.GetItem(itemId).Returns(new ItemData { Num = itemId, Countable = 1, Weight = 1, Duration = 10 }),
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId: 29, accountId: 39);
        session.Name = "Claimer";
        session.Hp = 100;
        session.Money = purse;
        session.Stats.MaxWeight = 100;
        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(ShoppingMallLetterProtocol.StoreLetter);
        request.WriteByte(ShoppingMallLetterProtocol.LetterGetItem);
        request.WriteInt(letterId);
        request.ResetOffset();

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(client, request);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var letter = await db.MailBoxes.AsNoTracking().SingleAsync();
        var stored = await db.Characters.AsNoTracking().SingleAsync(c => c.Id == 29);
        if (saveFails)
        {
            session.Money.Should().Be(purse);
            session.Inventory.Should().NotContain(slot => slot.ItemId == itemId);
            letter.Status.Should().Be(ShoppingMallLetterProtocol.LetterStatusNew, "a failed claim leaves the letter to claim again");
            return;
        }

        stored.Money.Should().Be(purse + coins, "the coins are saved with the claim, not at some later save");
        stored.Items.Should().Equal(session.SerializeItems());
        letter.Status.Should().Be(ShoppingMallLetterProtocol.LetterStatusRead);
    }

    [Fact]
    public async Task ALetterWhoseCoinsWouldPassTheCapIsNotClaimed()
    {
        const int letterId = 1003;
        const int coins = 500;
        using var provider = CreateProvider(db =>
        {
            db.Characters.Add(new Character { Id = 29, AccountId = 39, Name = "Claimer" });
            db.MailBoxes.Add(new MailBox
            {
                LetterId = letterId, RecipientId = "Claimer", SenderId = "Sender", Subject = "Coins", Message = "Take them",
                Type = ShoppingMallPacketWriter.LetterTypeWithItem, Status = ShoppingMallLetterProtocol.LetterStatusNew,
                Coins = coins, SendDate = DateTime.UtcNow,
            });
        });
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId: 29, accountId: 39);
        session.Name = "Claimer";
        session.Hp = 100;
        session.Money = ExchangePacketConstants.CoinMax - coins + 1;
        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(ShoppingMallLetterProtocol.StoreLetter);
        request.WriteByte(ShoppingMallLetterProtocol.LetterGetItem);
        request.WriteInt(letterId);
        request.ResetOffset();

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(client, request);

        session.Money.Should().Be(ExchangePacketConstants.CoinMax - coins + 1, "a purse never passes the coin cap");
        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().MailBoxes.AsNoTracking().SingleAsync())
            .Status.Should().Be(ShoppingMallLetterProtocol.LetterStatusNew, "the coins wait until they fit");
    }

    private sealed class ClaimFaults : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool Armed { get; init; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Armed && eventData.Context?.ChangeTracker.Entries<MailBox>().Any(entry => entry.State == EntityState.Modified) == true
                ? throw new DbUpdateException("the database went away")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static byte LetterResult(List<Packet> sent)
    {
        var response = sent.Last(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_SHOPPING_MALL);
        response.ResetOffset();
        response.ReadByte();
        response.ReadByte();
        return response.ReadByte();
    }

    private sealed class LetterSaveFaults : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public Action Meanwhile { get; set; } = () => { };

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<MailBox>().Any(entry => entry.State == EntityState.Added) != true)
                return base.SavingChangesAsync(eventData, result, cancellationToken);

            Meanwhile();
            throw new DbUpdateException("the database went away");
        }
    }
}
