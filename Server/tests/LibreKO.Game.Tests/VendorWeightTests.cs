using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class VendorWeightTests : GameTestBase
{
    private const int WaterOfFavors = 389014000;
    private const short WaterWeight = 45;
    private const int WaterPrice = 4_900;
    private const int PotionOfSagacity = 389018000;
    private const short SagacityWeight = 13;
    private const ushort SagacityCount = 102;
    private const short PotionDuration = 1;
    private const int Shama = 8161;
    private const int SellingGroup = 253000;
    private const int CharacterId = 34001;
    private const int AccountId = 34002;
    private const byte Karus = 1;
    private const short KarusMage = 105;
    private const byte CharacterLevel = 79;
    private const byte CharacterStrength = 147;
    private const byte CharacterStamina = 50;
    private const byte CharacterIntelligence = 100;
    private const double MageCoefficient = 0.01;
    private const int StartingMoney = 40_515_957;
    private const int StartingWeight = SagacityWeight * SagacityCount;
    private const int CarryCapacity = 11_300;
    private const ushort LargestFittingPurchase = 221;
    private const ushort ScreenshotPurchase = 400;
    private const byte Buy = 1;
    private const byte PurchasePosition = 0;
    private const byte ExistingPosition = 1;
    private const byte VendorLine = 0;
    private const byte WaterListIndex = 4;

    [Fact]
    public async Task FourHundredWatersExceedCapacityEvenWithAnEmptyInventory()
    {
        var (provider, session, sent, npc) = Arrange(withExistingPotions: false);
        using (provider)
        {
            var inventoryBefore = session.SerializeItems();
            session.Stats.ItemWeight.Should().Be(0);

            await BuyWater(provider, session, npc, ScreenshotPurchase);

            session.Money.Should().Be(StartingMoney);
            session.SerializeItems().Should().Equal(inventoryBefore);
            session.Stats.ItemWeight.Should().Be(0);
            session.Stats.MaxWeight.Should().Be(CarryCapacity);
            AssertOverweight(sent);
        }
    }

    [Theory]
    [InlineData(LargestFittingPurchase + 1)]
    [InlineData(ScreenshotPurchase)]
    public async Task OverweightWaterPurchasesPreserveExistingInventoryAndCoins(ushort count)
    {
        var (provider, session, sent, npc) = Arrange();
        using (provider)
        {
            var inventoryBefore = session.SerializeItems();
            session.Stats.ItemWeight.Should().Be(StartingWeight);

            await BuyWater(provider, session, npc, count);

            session.Money.Should().Be(StartingMoney);
            session.SerializeItems().Should().Equal(inventoryBefore);
            session.Stats.ItemWeight.Should().Be(StartingWeight);
            session.Stats.MaxWeight.Should().Be(CarryCapacity);
            AssertOverweight(sent);
        }
    }

    [Fact]
    public async Task TheLargestFittingWaterPurchaseChargesCoinsAndRecalculatesTheActualWeight()
    {
        var (provider, session, sent, npc) = Arrange();
        using (provider)
        {
            var expectedPrice = WaterPrice * LargestFittingPurchase;
            var expectedWeight = StartingWeight + WaterWeight * LargestFittingPurchase;

            await BuyWater(provider, session, npc, LargestFittingPurchase);

            session.Money.Should().Be(StartingMoney - expectedPrice);
            var purchased = session.Inventory[InventoryConstants.InventoryStart + PurchasePosition];
            purchased.ItemId.Should().Be(WaterOfFavors);
            purchased.Count.Should().Be(LargestFittingPurchase);
            purchased.Durability.Should().Be(PotionDuration);
            var existing = session.Inventory[InventoryConstants.InventoryStart + ExistingPosition];
            existing.ItemId.Should().Be(PotionOfSagacity);
            existing.Count.Should().Be(SagacityCount);
            existing.Durability.Should().Be(PotionDuration);
            session.Stats.ItemWeight.Should().Be(expectedWeight);
            session.Stats.MaxWeight.Should().Be(CarryCapacity);
            expectedWeight.Should().BeLessThanOrEqualTo(CarryCapacity);
            (expectedWeight + WaterWeight).Should().BeGreaterThan(CarryCapacity);

            var reply = sent.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_TRADE);
            reply.ResetOffset();
            reply.ReadByte().Should().Be((byte)ItemTradeResult.Traded);
            reply.ReadInt().Should().Be(StartingMoney - expectedPrice);
            reply.ReadInt().Should().Be(expectedPrice);
            reply.RemainingBytes.Should().Be(0);
        }
    }

    private static (ServiceProvider Provider, UserSession Session, List<Packet> Sent, NpcInstance Npc) Arrange(
        bool withExistingPotions = true)
    {
        var provider = CreateProvider(_ => { }, gameData =>
        {
            var coefficient = CreateBasicCoefficient(KarusMage);
            coefficient.Hp = MageCoefficient;
            coefficient.Mp = MageCoefficient;
            gameData.GetCoefficient(KarusMage).Returns(coefficient);
            gameData.GetItem(WaterOfFavors).Returns(new ItemData
            {
                Num = WaterOfFavors, BuyPrice = WaterPrice, Weight = WaterWeight,
                Countable = 1, Duration = PotionDuration
            });
            gameData.GetItem(PotionOfSagacity).Returns(new ItemData
            {
                Num = PotionOfSagacity, Weight = SagacityWeight,
                Countable = 1, Duration = PotionDuration
            });
        });
        var sent = new List<Packet>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(packet => sent.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var manager = provider.GetRequiredService<SessionManager>();
        var session = manager.CreateSession(client, CharacterId, AccountId);
        session.Hp = 100;
        session.Class = KarusMage;
        session.Level = CharacterLevel;
        session.Strength = CharacterStrength;
        session.Stamina = CharacterStamina;
        session.Intelligence = CharacterIntelligence;
        session.Money = StartingMoney;
        session.ZoneId = Karus;
        session.X = 10;
        session.Z = 10;
        if (withExistingPotions)
        {
            var existing = session.Inventory[InventoryConstants.InventoryStart + ExistingPosition];
            existing.ItemId = PotionOfSagacity;
            existing.Count = SagacityCount;
            existing.Durability = PotionDuration;
        }
        session.RecalculateStatsWithBuffs(provider.GetRequiredService<IGameDataService>());
        session.Stats.MaxWeight.Should().Be(CarryCapacity);
        var npc = manager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = Shama, SellingGroup = SellingGroup, NpcType = NpcData.TypeTradeMerchant,
            ZoneId = Karus, X = session.X, Z = session.Z, Hp = 1, MaxHp = 1
        });
        return (provider, session, sent, npc);
    }

    private static Task BuyWater(IServiceProvider provider, UserSession session, NpcInstance npc, ushort count)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_TRADE);
        packet.WriteByte(Buy);
        packet.WriteInt(SellingGroup);
        packet.WriteInt(npc.UniqueId);
        packet.WriteByte(1);
        packet.WriteInt(WaterOfFavors);
        packet.WriteByte(PurchasePosition);
        packet.WriteUShort(count);
        packet.WriteByte(VendorLine);
        packet.WriteByte(WaterListIndex);
        return provider.GetRequiredService<IItemPacketCoordinator>().HandleTradeAsync(session.Client, packet);
    }

    private static void AssertOverweight(List<Packet> sent)
    {
        var reply = sent.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_TRADE);
        reply.ResetOffset();
        reply.ReadByte().Should().Be((byte)ItemTradeResult.Refused);
        reply.ReadByte().Should().Be((byte)ItemTradeRefusal.InventoryFull);
        reply.RemainingBytes.Should().Be(0);
        sent.Should().NotContain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_WEIGHT_CHANGE);
    }
}
