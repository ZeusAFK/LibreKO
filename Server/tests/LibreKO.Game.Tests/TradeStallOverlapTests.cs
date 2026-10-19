using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

// No one is ever trading and keeping (or setting up) a stall at once, or trading or keeping a
// stall while gathering: each of those counts on the bag staying as it is.
public class TradeStallOverlapTests : GameTestBase
{
    private const int AskerId = 97001;
    private const int PartnerId = 97002;
    private const int Ware = 379_080_000;

    [Fact]
    public async Task ATradeCannotBeAskedForWhileSettingUpAStall()
    {
        using var provider = CreateProvider(_ => { });
        var (asker, partner) = Pair(provider);
        asker.Trade.IsSellingMerchantPreparing = true;

        await RequestAsync(provider, asker, partner);

        asker.Trade.IsTrading.Should().BeFalse();
        partner.Trade.IsTrading.Should().BeFalse();
    }

    [Fact]
    public async Task ATradeCannotBeAskedOfSomeoneSettingUpAStall()
    {
        using var provider = CreateProvider(_ => { });
        var (asker, partner) = Pair(provider);
        partner.Trade.IsBuyingMerchantPreparing = true;

        await RequestAsync(provider, asker, partner);

        asker.Trade.IsTrading.Should().BeFalse();
        partner.Trade.IsTrading.Should().BeFalse();
    }

    [Fact]
    public async Task ATradeCannotBeAskedForWhileGathering()
    {
        using var provider = CreateProvider(_ => { });
        var (asker, partner) = Pair(provider);
        asker.IsMining = true;

        await RequestAsync(provider, asker, partner);

        asker.Trade.IsTrading.Should().BeFalse();
        partner.Trade.IsTrading.Should().BeFalse();
    }

    [Fact]
    public async Task AnAnswerFromSomeoneSettingUpAStallTurnsTheTradeDown()
    {
        using var provider = CreateProvider(_ => { });
        var (asker, partner) = Pair(provider);
        await RequestAsync(provider, asker, partner);
        partner.Trade.IsTrading.Should().BeTrue();
        partner.Trade.IsSellingMerchantPreparing = true;

        var agree = new Packet(GameOpcodes.GS_EXCHANGE);
        agree.WriteByte(ExchangePacketConstants.ExchangeAgree);
        agree.WriteByte(1);
        await SendAsync(provider, partner, agree);

        asker.Trade.IsTrading.Should().BeFalse();
        partner.Trade.IsTrading.Should().BeFalse();
    }

    [Fact]
    public async Task AStallCannotOpenForBusinessWhileItsOwnerTrades()
    {
        using var provider = CreateProvider(_ => { });
        var (owner, partner) = Pair(provider);
        owner.Trade.ExchangeUser = partner.CharacterId;
        partner.Trade.ExchangeUser = owner.CharacterId;
        owner.Trade.IsSellingMerchantPreparing = true;
        owner.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = Ware, Count = 1, Price = 100, OriginalSlot = InventoryConstants.InventoryStart,
        };

        var insert = new Packet(GameOpcodes.GS_MERCHANT);
        insert.WriteByte((byte)MerchantSubOpcode.Insert);
        insert.WriteString("cheap");
        await SendMerchantAsync(provider, owner, insert);

        owner.Trade.IsMerchanting.Should().BeFalse();
    }

    [Theory]
    [InlineData(MerchantSubOpcode.Open)]
    [InlineData(MerchantSubOpcode.BuyOpen)]
    public async Task AStallCannotBeSetUpWhileGathering(MerchantSubOpcode open)
    {
        using var provider = CreateProvider(_ => { });
        var (owner, _) = Pair(provider);
        owner.IsMining = true;

        var packet = new Packet(GameOpcodes.GS_MERCHANT);
        packet.WriteByte((byte)open);
        await SendMerchantAsync(provider, owner, packet);

        owner.Trade.IsMerchantPreparing.Should().BeFalse();
    }

    [Fact]
    public async Task ABuyingStallCannotOpenForBusinessWhileItsOwnerGathers()
    {
        using var provider = CreateProvider(_ => { }, gameData => gameData.GetItem(Ware).Returns(new ItemData
        {
            Num = Ware, Countable = 1, Weight = 1,
        }));
        var (owner, _) = Pair(provider);
        owner.Money = 10_000;
        owner.Trade.IsBuyingMerchantPreparing = true;
        owner.IsMining = true;

        var insert = new Packet(GameOpcodes.GS_MERCHANT);
        insert.WriteByte((byte)MerchantSubOpcode.BuyInsert);
        insert.WriteByte(1);
        insert.WriteInt(Ware);
        insert.WriteUShort(1);
        insert.WriteInt(100);
        await SendMerchantAsync(provider, owner, insert);

        owner.Trade.IsMerchanting.Should().BeFalse();
        owner.Money.Should().Be(10_000);
    }

    private static (UserSession Asker, UserSession Partner) Pair(ServiceProvider provider)
    {
        var sessionManager = provider.GetRequiredService<SessionManager>();
        return (Player(sessionManager, AskerId), Player(sessionManager, PartnerId));
    }

    private static UserSession Player(SessionManager sessionManager, int characterId)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessionManager.CreateSession(client, characterId, characterId + 1000);
        session.Name = $"Player{characterId}";
        session.Nation = AccountNation.Karus;
        session.ZoneId = BattleZoneManager.ZONE_MORADON;
        session.X = 816f;
        session.Z = 531f;
        session.Hp = 100;
        session.MaxHp = 100;
        session.Level = 80;
        sessionManager.Regions.AddToRegion(session);
        return session;
    }

    private static Task RequestAsync(ServiceProvider provider, UserSession asker, UserSession partner)
    {
        var request = new Packet(GameOpcodes.GS_EXCHANGE);
        request.WriteByte(ExchangePacketConstants.ExchangeRequest);
        request.WriteInt(partner.CharacterId);
        return SendAsync(provider, asker, request);
    }

    private static Task SendAsync(ServiceProvider provider, UserSession session, Packet packet)
    {
        packet.ResetOffset();
        return provider.GetRequiredService<IExchangePacketCoordinator>().HandleAsync(session.Client, packet);
    }

    private static Task SendMerchantAsync(ServiceProvider provider, UserSession session, Packet packet)
    {
        packet.ResetOffset();
        return provider.GetRequiredService<IMerchantPacketCoordinator>().HandleAsync(session.Client, packet);
    }
}
