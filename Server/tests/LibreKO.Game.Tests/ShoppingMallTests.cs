using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task ShoppingMallPacketCoordinator_HandleAsync_BuyUsesAccountCurrencyAndGivesItem()
    {
        const int itemId = 800079000;

        using var provider = CreateProvider(
            db =>
            {
                db.PusItems.Add(new PusItemData
                {
                    Id = 1,
                    ItemId = itemId,
                    Name = "HP Scroll 60%",
                    Price = 500,
                    Category = 1,
                    Description = "HP recovery",
                });
            },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Race = 1,
                    Countable = 1,
                    Duration = 0,
                    Kind = 1,
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 27, accountId: 37);
        session.Name = "Buyer";
        session.KnightCash = 1000;

        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(8);
        request.WriteByte(1);
        request.WriteByte(1);
        request.WriteInt(1);
        request.WriteByte(1);

        var coordinator = provider.GetRequiredService<IShoppingMallPacketCoordinator>();
        await coordinator.HandleAsync(client, request);

        session.KnightCash.Should().Be(500);
        var index = Array.FindIndex(session.Inventory, slot => slot.ItemId == itemId);
        index.Should().BeGreaterThanOrEqualTo(InventoryConstants.SlotMax);
        session.Inventory[index].Count.Should().Be(1);

        sentPackets.Should().HaveCount(2);
        var sentPacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_SHOPPING_MALL);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(8);
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadInt().Should().Be(500);
    }

    [Fact]
    public async Task ShoppingMallPacketCoordinator_HandleAsync_BuyUsesRequestedCatalogEntry()
    {
        const int itemId = 800079000;

        using var provider = CreateProvider(
            db => db.PusItems.AddRange(
                new PusItemData
                {
                    Id = 2,
                    ItemId = itemId,
                    Name = "Standard listing",
                    Price = 1000,
                    Category = 1,
                    Description = "Standard listing",
                },
                new PusItemData
                {
                    Id = 3,
                    ItemId = itemId,
                    Name = "Sale listing",
                    Price = 100,
                    Category = 3,
                    Description = "Sale listing",
                }),
            gameData => gameData.GetItem(itemId).Returns(new ItemData { Num = itemId, Countable = 1, Duration = 1 }));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, 29, 39);
        session.KnightCash = 1000;
        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(8);
        request.WriteByte(1);
        request.WriteByte(1);
        request.WriteInt(2);
        request.WriteByte(1);

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(client, request);

        session.KnightCash.Should().Be(0);
        sentPackets.Should().HaveCount(2);
    }

    [Fact]
    public async Task ShoppingMallPacketCoordinator_HandleAsync_BuyRejectsMultipleNonCountableItems()
    {
        const int itemId = 800079000;

        using var provider = CreateProvider(
            db => db.PusItems.Add(new PusItemData
            {
                Id = 4,
                ItemId = itemId,
                Name = "Non-countable item",
                Price = 100,
                Category = 1,
                Description = "Non-countable item",
            }),
            gameData => gameData.GetItem(itemId).Returns(new ItemData { Num = itemId, Countable = 0, Duration = 1 }));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, 29, 39);
        session.KnightCash = 1000;
        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(8);
        request.WriteByte(1);
        request.WriteByte(1);
        request.WriteInt(4);
        request.WriteByte(2);

        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(client, request);

        session.KnightCash.Should().Be(1000);
        session.Inventory.Should().OnlyContain(slot => slot.IsEmpty);
        sentPackets.Should().ContainSingle();
    }

    [Fact]
    public async Task ShoppingMallPacketCoordinator_HandleAsync_LetterSendUsesInventoryRelativeSlot()
    {
        const int itemId = 910001000;

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
    public async Task ShoppingMallPacketCoordinator_HandleAsync_LetterSendSupportsPartialStackDeduction()
    {
        const int itemId = 810003000;
        using var provider = CreateProvider(
            db =>
            {
                db.Characters.Add(new Character
                {
                    Id = 26,
                    AccountId = 36,
                    Name = "Sender",
                    Race = 1,
                    Class = 101,
                    Level = 80,
                    MapId = 1,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
                db.Characters.Add(new Character
                {
                    Id = 27,
                    AccountId = 37,
                    Name = "Recipient",
                    Race = 1,
                    Class = 101,
                    Level = 80,
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
                    Race = 1,
                    Countable = 1,
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 26, accountId: 36);
        session.Name = "Sender";
        session.Money = 20000;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = itemId;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 25;
        session.Inventory[InventoryConstants.InventoryStart].Count = 50;

        var request = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        request.WriteByte(6);
        request.WriteByte(6);
        request.WriteSByteString("Recipient");
        request.WriteSByteString("Subject");
        request.WriteByte(2);
        request.WriteInt(itemId);
        request.WriteByte(0);
        request.WriteInt(15); // Send 15 out of 50
        request.WriteString("Message");

        var coordinator = provider.GetRequiredService<IShoppingMallPacketCoordinator>();
        await coordinator.HandleAsync(client, request);

        var response = sentPackets.Last(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_SHOPPING_MALL);
        response.ResetOffset();
        response.ReadByte().Should().Be(6);
        response.ReadByte().Should().Be(6);
        response.ReadByte().Should().Be(1);

        session.Money.Should().Be(10000);
        session.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(35);
        session.Inventory[InventoryConstants.InventoryStart].IsEmpty.Should().BeFalse();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var letter = await db.MailBoxes.SingleAsync();
        letter.Coins.Should().Be(0);
        letter.ItemId.Should().Be(itemId);
        letter.Count.Should().Be(15);
    }

    [Fact]
    public async Task ShoppingMallPacketCoordinator_HandleAsync_LetterGetItemStacksExistingItemUsingRelativeSlot()
    {
        const int itemId = 810003000;
        const int letterId = 1001;

        using var provider = CreateProvider(
            db =>
            {
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
}
