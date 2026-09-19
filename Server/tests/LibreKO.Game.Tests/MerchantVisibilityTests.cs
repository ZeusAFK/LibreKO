using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class MerchantVisibilityTests : GameTestBase
{
    [Fact]
    public async Task ReqUserInTellsAnArrivingPlayerAboutTheStallsAlreadyOpen()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var merchantClient = Substitute.For<IClient>();
        merchantClient.Id.Returns(Guid.NewGuid());
        merchantClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var merchant = sessionManager.CreateSession(merchantClient, characterId: 8101, accountId: 9101);
        merchant.Name = "Seller";
        merchant.ZoneId = 21;
        merchant.X = 100;
        merchant.Z = 100;
        merchant.Trade.MerchantState = MerchantMode.Buying;
        sessionManager.Regions.AddToRegion(merchant);

        var arrivalClient = Substitute.For<IClient>();
        arrivalClient.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        arrivalClient.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var arrival = sessionManager.CreateSession(arrivalClient, characterId: 8102, accountId: 9102);
        arrival.Name = "Arrival";
        arrival.ZoneId = 21;
        arrival.X = 102;
        arrival.Z = 100;
        sessionManager.Regions.AddToRegion(arrival);

        var request = new Packet(GameOpcodes.GS_REQ_USERIN);
        await provider.GetRequiredService<IWorldVisibilityService>()
            .HandleReqUserInAsync(arrivalClient, request);

        var stalls = sent.SingleOrDefault(p => p.GetOpcode() == (byte)GameOpcodes.GS_MERCHANT_INOUT);
        stalls.Should().NotBeNull("a player arriving next to an open stall must be told it is there");

        stalls!.ResetOffset();
        stalls.ReadByte().Should().Be((byte)MerchantInOut.StallsInView);
        stalls.ReadShort().Should().Be(1);
        stalls.ReadInt().Should().Be(merchant.CharacterId);
        stalls.ReadByte().Should().Be(1);
        stalls.ReadByte().Should().Be(0);
        stalls.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task ReqUserInSaysNothingWhenNobodyNearbyIsMerchanting()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var idleClient = Substitute.For<IClient>();
        idleClient.Id.Returns(Guid.NewGuid());
        idleClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var idle = sessionManager.CreateSession(idleClient, characterId: 8103, accountId: 9103);
        idle.ZoneId = 21;
        sessionManager.Regions.AddToRegion(idle);

        var arrivalClient = Substitute.For<IClient>();
        arrivalClient.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        arrivalClient.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var arrival = sessionManager.CreateSession(arrivalClient, characterId: 8104, accountId: 9104);
        arrival.ZoneId = 21;
        sessionManager.Regions.AddToRegion(arrival);

        await provider.GetRequiredService<IWorldVisibilityService>()
            .HandleReqUserInAsync(arrivalClient, new Packet(GameOpcodes.GS_REQ_USERIN));

        sent.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_MERCHANT_INOUT);
    }

    [Fact]
    public async Task StallListRequestAnswersWithTheStallsKindAndContents()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var merchantClient = Substitute.For<IClient>();
        merchantClient.Id.Returns(Guid.NewGuid());
        merchantClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var merchant = sessionManager.CreateSession(merchantClient, characterId: 8201, accountId: 9201);
        merchant.ZoneId = 21;
        merchant.Trade.MerchantState = MerchantMode.Buying;
        merchant.Trade.BuyMerchantItems[0] = new MerchantItem
        {
            ItemId = 379080000, Count = 5, Price = 1000,
        };
        sessionManager.Regions.AddToRegion(merchant);

        var viewerClient = Substitute.For<IClient>();
        viewerClient.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        viewerClient.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var viewer = sessionManager.CreateSession(viewerClient, characterId: 8202, accountId: 9202);
        viewer.ZoneId = 21;
        sessionManager.Regions.AddToRegion(viewer);

        var request = new Packet(GameOpcodes.GS_MERCHANT);
        request.WriteByte((byte)MerchantSubOpcode.StallList);
        request.WriteInt(merchant.CharacterId);

        await provider.GetRequiredService<IMerchantPacketCoordinator>()
            .HandleAsync(viewerClient, request);

        var reply = sent.SingleOrDefault(p => p.GetOpcode() == (byte)GameOpcodes.GS_MERCHANT);
        reply.Should().NotBeNull("the client asks for this to learn what to draw on the counter");

        reply!.ResetOffset();
        reply.ReadByte().Should().Be((byte)MerchantSubOpcode.StallList);
        reply.ReadByte().Should().Be((byte)BuyingMerchantResult.Accepted);
        reply.ReadInt().Should().Be(merchant.CharacterId);
        reply.ReadByte().Should().Be(1);
        reply.ReadByte().Should().Be(0);
        reply.ReadInt().Should().Be(379080000);
    }

    [Fact]
    public async Task SellingIntoABuyingStallMovesGoldAndTellsBothSides()
    {
        const int itemId = 379080000;
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetItem(itemId).Returns(new ItemData
            {
                Num = itemId, Countable = 1, Duration = 0,
            }));

        var sessionManager = provider.GetRequiredService<SessionManager>();

        var merchantClient = Substitute.For<IClient>();
        merchantClient.Id.Returns(Guid.NewGuid());
        var merchantSent = new List<Packet>();
        merchantClient.SendPacket(Arg.Do<Packet>(merchantSent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var merchant = sessionManager.CreateSession(merchantClient, characterId: 8301, accountId: 9301);
        merchant.ZoneId = 21;
        merchant.Hp = 100;
        merchant.Money = 50_000;
        merchant.Trade.MerchantState = MerchantMode.Buying;
        merchant.Trade.BuyMerchantItems[0] = new MerchantItem
        {
            ItemId = itemId, Count = 10, Price = 1_000, Durability = 0,
        };
        sessionManager.Regions.AddToRegion(merchant);

        var sellerClient = Substitute.For<IClient>();
        sellerClient.Id.Returns(Guid.NewGuid());
        var sellerSent = new List<Packet>();
        sellerClient.SendPacket(Arg.Do<Packet>(sellerSent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var seller = sessionManager.CreateSession(sellerClient, characterId: 8302, accountId: 9302);
        seller.ZoneId = 21;
        seller.Hp = 100;
        seller.Money = 200;
        seller.Trade.MerchantTargetUserId = merchant.CharacterId;
        seller.Inventory[InventoryConstants.SlotMax].ItemId = itemId;
        seller.Inventory[InventoryConstants.SlotMax].Count = 4;
        sessionManager.Regions.AddToRegion(seller);

        var buy = new Packet(GameOpcodes.GS_MERCHANT);
        buy.WriteByte((byte)MerchantSubOpcode.BuyBuy);
        buy.WriteByte(0);
        buy.WriteByte(0);
        buy.WriteUShort(3);

        await provider.GetRequiredService<IMerchantPacketCoordinator>().HandleAsync(sellerClient, buy);

        seller.Money.Should().Be(200 + 3_000, "the seller is paid for what the stall took");
        merchant.Money.Should().Be(50_000 - 3_000, "the stall pays out of the owner's gold");
        seller.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
        merchant.Trade.BuyMerchantItems[0].Count.Should().Be(7);

        sellerSent.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_GOLD_CHANGE,
            "the seller's balance has to refresh on screen");
        merchantSent.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_GOLD_CHANGE,
            "the stall owner's balance has to refresh on screen");
    }

    [Fact]
    public async Task RetailAsksPermissionBeforeItRequestsAStallsContents()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var merchant = sessionManager.CreateSession(Client(out _), characterId: 8501, accountId: 9501);
        merchant.ZoneId = 21;
        merchant.Hp = 100;
        merchant.Trade.MerchantState = MerchantMode.Selling;
        merchant.Trade.MerchantItems[0] = new MerchantItem { ItemId = 379080000, Count = 1, Price = 10 };
        sessionManager.Regions.AddToRegion(merchant);

        var viewerClient = Client(out var sent);
        var viewer = sessionManager.CreateSession(viewerClient, characterId: 8502, accountId: 9502);
        viewer.ZoneId = 21;
        viewer.Hp = 100;
        sessionManager.Regions.AddToRegion(viewer);

        merchant.Name = "Seller";
        merchant.Trade.MerchantAdvert = "cheap gear";

        await Gate(provider, viewerClient, MerchantSubOpcode.SellingStallRequest, merchant.CharacterId);

        var reply = Find(sent, MerchantSubOpcode.SellingStallRequest);
        reply.Should().NotBeNull("without this answer the retail client never asks for the contents");
        reply!.ReadUShort().Should().Be(1, "the window handler reads a 16-bit result, not a byte");
        reply.ReadInt().Should().Be(merchant.CharacterId);
        reply.ReadSByteString().Should().Be("cheap gear");
        reply.RemainingBytes.Should().Be(0);
        merchant.Trade.MerchantViewerId.Should().Be(viewer.CharacterId);
    }

    [Fact]
    public async Task ContentsGateIsASingleByteUnlikeTheWindowGate()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var merchant = sessionManager.CreateSession(Client(out _), characterId: 8511, accountId: 9511);
        merchant.ZoneId = 21;
        merchant.Hp = 100;
        merchant.Trade.MerchantState = MerchantMode.Selling;
        sessionManager.Regions.AddToRegion(merchant);

        var viewerClient = Client(out var sent);
        var viewer = sessionManager.CreateSession(viewerClient, characterId: 8512, accountId: 9512);
        viewer.ZoneId = 21;
        viewer.Hp = 100;
        sessionManager.Regions.AddToRegion(viewer);

        var packet = new Packet(GameOpcodes.GS_MERCHANT);
        packet.WriteByte((byte)MerchantSubOpcode.SellingStallOpen);
        packet.WriteInt(merchant.CharacterId);
        packet.WriteByte(0);
        await provider.GetRequiredService<IMerchantPacketCoordinator>().HandleAsync(viewerClient, packet);

        var reply = Find(sent, MerchantSubOpcode.SellingStallOpen);
        reply.Should().NotBeNull();
        reply!.ReadByte().Should().Be(1);
        reply.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task ASecondShopperIsTurnedAwayWhileSomeoneElseIsBrowsing()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var merchant = sessionManager.CreateSession(Client(out _), characterId: 8503, accountId: 9503);
        merchant.ZoneId = 21;
        merchant.Hp = 100;
        merchant.Trade.MerchantState = MerchantMode.Selling;
        sessionManager.Regions.AddToRegion(merchant);

        var firstClient = Client(out _);
        var first = sessionManager.CreateSession(firstClient, characterId: 8504, accountId: 9504);
        first.ZoneId = 21;
        first.Hp = 100;
        sessionManager.Regions.AddToRegion(first);

        var secondClient = Client(out var secondSent);
        var second = sessionManager.CreateSession(secondClient, characterId: 8505, accountId: 9505);
        second.ZoneId = 21;
        second.Hp = 100;
        sessionManager.Regions.AddToRegion(second);

        await Gate(provider, firstClient, MerchantSubOpcode.SellingStallRequest, merchant.CharacterId);
        await Gate(provider, secondClient, MerchantSubOpcode.SellingStallRequest, merchant.CharacterId);

        Find(secondSent, MerchantSubOpcode.SellingStallRequest)!.ReadUShort()
            .Should().Be(MerchantPacketWriter.StallOpenRefused,
                "one shopper at a time; 7 is the code the client prints a message for");
        merchant.Trade.MerchantViewerId.Should().Be(first.CharacterId);
    }

    [Fact]
    public async Task LeavingAStallFreesItForTheNextShopper()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var merchant = sessionManager.CreateSession(Client(out _), characterId: 8506, accountId: 9506);
        merchant.ZoneId = 21;
        merchant.Hp = 100;
        merchant.Trade.MerchantState = MerchantMode.Selling;
        sessionManager.Regions.AddToRegion(merchant);

        var firstClient = Client(out _);
        var first = sessionManager.CreateSession(firstClient, characterId: 8507, accountId: 9507);
        first.ZoneId = 21;
        first.Hp = 100;
        sessionManager.Regions.AddToRegion(first);

        var secondClient = Client(out var secondSent);
        var second = sessionManager.CreateSession(secondClient, characterId: 8508, accountId: 9508);
        second.ZoneId = 21;
        second.Hp = 100;
        sessionManager.Regions.AddToRegion(second);

        var coordinator = provider.GetRequiredService<IMerchantPacketCoordinator>();
        await Gate(provider, firstClient, MerchantSubOpcode.SellingStallRequest, merchant.CharacterId);

        var leave = new Packet(GameOpcodes.GS_MERCHANT);
        leave.WriteByte((byte)MerchantSubOpcode.TradeCancel);
        await coordinator.HandleAsync(firstClient, leave);

        await Gate(provider, secondClient, MerchantSubOpcode.SellingStallRequest, merchant.CharacterId);

        Find(secondSent, MerchantSubOpcode.SellingStallRequest)!.ReadUShort().Should().Be(1);
        merchant.Trade.MerchantViewerId.Should().Be(second.CharacterId);
    }

    private static IClient Client(out List<Packet> sent)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var captured = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(captured.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        sent = captured;
        return client;
    }

    private static Task Gate(
        ServiceProvider provider, IClient client, MerchantSubOpcode sub, int merchantId)
    {
        var packet = new Packet(GameOpcodes.GS_MERCHANT);
        packet.WriteByte((byte)sub);
        packet.WriteInt(merchantId);
        return provider.GetRequiredService<IMerchantPacketCoordinator>().HandleAsync(client, packet);
    }

    private static Packet? Find(List<Packet> sent, MerchantSubOpcode sub)
    {
        for (var i = sent.Count - 1; i >= 0; i--)
        {
            var packet = sent[i];
            if (packet.GetOpcode() != (byte)GameOpcodes.GS_MERCHANT) continue;
            packet.ResetOffset();
            if (packet.ReadByte() == (byte)sub) return packet;
        }
        return null;
    }
}
