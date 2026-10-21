using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ExchangeTests : GameTestBase
{
    private const int AskerId = 91001;
    private const int PartnerId = 91002;
    private const int TradedItemId = 700001100;
    private const byte OfferedSlot = 3;
    private const byte ExchangeAdd = 3;
    private const byte ExchangeAgree = 2;
    private const float FarAway = 500f;
    private const byte RaceUntradeable = 20;

    [Theory]
    [InlineData(ItemFlag.Unsealed, true)]
    [InlineData(ItemFlag.NotBound, true)]
    [InlineData(ItemFlag.Sealed, false)]
    [InlineData(ItemFlag.Bound, false)]
    [InlineData(ItemFlag.Rented, false)]
    [InlineData(ItemFlag.Duplicate, false)]
    [InlineData(ItemFlag.CharacterSeal, false)]
    public async Task OnlyAFreeItemMayBeOffered(ItemFlag flag, bool accepted)
    {
        using var provider = CreateProvider(_ => { }, StockOneTradableItem);
        var (asker, partner, askerClient, _) = OpenTrade(provider);

        var slot = asker.Inventory[InventoryConstants.SlotMax + OfferedSlot];
        slot.ItemId = TradedItemId;
        slot.Count = 1;
        slot.Flag = (byte)flag;

        Packet? reply = null;
        askerClient.SendPacket(Arg.Do<Packet>(p => reply = p), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await AddAsync(provider, askerClient, TradedItemId, 1);

        reply.Should().NotBeNull();
        reply!.ResetOffset();
        reply.ReadByte().Should().Be(ExchangeAdd);
        reply.ReadByte().Should().Be(accepted ? ExchangePacketWriter.Succeeded : ExchangePacketWriter.Failed);
        asker.Trade.ExchangeItemList.Should().HaveCount(accepted ? 1 : 0);
    }

    [Fact]
    public async Task AnItemItsOwnTableRefusesToTradeStaysPut()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetItem(TradedItemId).Returns(new ItemData
            {
                Num = TradedItemId,
                Weight = 10,
                Race = RaceUntradeable,
            }));

        var (asker, _, askerClient, _) = OpenTrade(provider);
        var slot = asker.Inventory[InventoryConstants.SlotMax + OfferedSlot];
        slot.ItemId = TradedItemId;
        slot.Count = 1;

        await AddAsync(provider, askerClient, TradedItemId, 1);

        asker.Trade.ExchangeItemList.Should().BeEmpty();
        slot.Count.Should().Be(1, "a refused offer must not leave the inventory short");
    }

    [Fact]
    public async Task TheOneWhoAskedCannotAnswerTheirOwnRequest()
    {
        using var provider = CreateProvider(_ => { }, StockOneTradableItem);
        var (asker, partner, askerClient, _) = OpenTrade(provider);

        var agree = new Packet(GameOpcodes.GS_EXCHANGE);
        agree.WriteByte(ExchangeAgree);
        agree.WriteByte(1);
        agree.ResetOffset();

        await provider.GetRequiredService<IExchangePacketCoordinator>().HandleAsync(askerClient, agree);

        asker.Trade.AskedForExchange.Should().BeTrue();
        partner.Trade.ExchangeItemList.Should().BeEmpty();
        asker.Trade.ExchangeItemList.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public async Task ASecondAnswerFromThePartnerLeavesAnOpenTradeAlone(byte answer)
    {
        using var provider = CreateProvider(_ => { }, StockOneTradableItem);
        var (asker, partner, askerClient, partnerClient) = PendingTrade(provider);
        var coordinator = provider.GetRequiredService<IExchangePacketCoordinator>();
        await coordinator.HandleAsync(partnerClient, Agree(1));
        var slot = asker.Inventory[InventoryConstants.SlotMax + OfferedSlot];
        slot.ItemId = TradedItemId;
        slot.Count = 1;
        await AddAsync(provider, askerClient, TradedItemId, 1);

        await coordinator.HandleAsync(partnerClient, Agree(answer));

        asker.Trade.ExchangeItemList.Should().ContainSingle("the offer stays on the table");
        asker.Trade.ExchangeUser.Should().Be(partner.CharacterId);
        partner.Trade.ExchangeUser.Should().Be(asker.CharacterId);
    }

    private static Packet Agree(byte answer)
    {
        var agree = new Packet(GameOpcodes.GS_EXCHANGE);
        agree.WriteByte(ExchangeAgree);
        agree.WriteByte(answer);
        agree.ResetOffset();
        return agree;
    }

    [Fact]
    public async Task AnAcceptTheServerRefusesClosesTheAcceptersWindow()
    {
        using var provider = CreateProvider(_ => { }, StockOneTradableItem);
        var (_, partner, _, partnerClient) = PendingTrade(provider);
        partner.X += FarAway;

        await provider.GetRequiredService<IExchangePacketCoordinator>().HandleAsync(partnerClient, Agree(1));

        partner.Trade.IsTrading.Should().BeFalse();
        partnerClient.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IClient.SendPacket))
            .Select(call => (Packet)call.GetArguments()[0]!)
            .Should().Contain(packet => IsCancel(packet), "the client opened its window on Accept, and only a cancel closes it");
    }

    private static bool IsCancel(Packet packet)
    {
        packet.ResetOffset();
        return packet.GetOpcode() == (byte)GameOpcodes.GS_EXCHANGE && packet.ReadByte() == ExchangePacketConstants.ExchangeCancel;
    }

    private static void StockOneTradableItem(IGameDataService gameData)
        => gameData.GetItem(TradedItemId).Returns(new ItemData { Num = TradedItemId, Weight = 10 });

    private static Task AddAsync(ServiceProvider provider, IClient client, int itemId, int count)
    {
        var packet = new Packet(GameOpcodes.GS_EXCHANGE);
        packet.WriteByte(ExchangeAdd);
        packet.WriteByte(OfferedSlot);
        packet.WriteInt(itemId);
        packet.WriteInt(count);
        packet.ResetOffset();
        return provider.GetRequiredService<IExchangePacketCoordinator>().HandleAsync(client, packet);
    }

    private static (UserSession Asker, UserSession Partner, IClient AskerClient, IClient PartnerClient)
        OpenTrade(ServiceProvider provider)
    {
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var askerClient = MakeClient(AskerId);
        var partnerClient = MakeClient(PartnerId);

        var asker = MakeSession(sessionManager, askerClient, AskerId);
        var partner = MakeSession(sessionManager, partnerClient, PartnerId);

        asker.Trade.ExchangeUser = partner.CharacterId;
        asker.Trade.AskedForExchange = true;
        partner.Trade.ExchangeUser = asker.CharacterId;
        asker.Trade.ExchangeAccepted = partner.Trade.ExchangeAccepted = true;

        return (asker, partner, askerClient, partnerClient);
    }

    private static (UserSession Asker, UserSession Partner, IClient AskerClient, IClient PartnerClient)
        PendingTrade(ServiceProvider provider)
    {
        var trade = OpenTrade(provider);
        trade.Asker.Trade.ExchangeAccepted = trade.Partner.Trade.ExchangeAccepted = false;
        return trade;
    }

    private static IClient MakeClient(int characterId)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.CharacterId.Returns(characterId);
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return client;
    }

    private static UserSession MakeSession(SessionManager sessionManager, IClient client, int characterId)
    {
        var session = sessionManager.CreateSession(client, characterId, accountId: characterId + 1000);
        session.Name = $"Trader{characterId}";
        session.Nation = AccountNation.Karus;
        session.ZoneId = BattleZoneManager.ZONE_MORADON;
        session.X = 816f;
        session.Z = 531f;
        session.Hp = 100;
        session.MaxHp = 100;
        return session;
    }
}
