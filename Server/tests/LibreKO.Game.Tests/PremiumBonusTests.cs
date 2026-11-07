using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class PremiumBonusTests
{
    private const byte GoldPremium = 5;
    private const byte FreeResetPremium = 12;
    private const byte LowLevel = 40;
    private const byte HighLevel = 70;

    private static IGameDataService GameData()
    {
        var data = Substitute.For<IGameDataService>();
        data.PremiumItemExpTable.Returns(
        [
            new PremiumItemExpData { Index = 1, Type = GoldPremium, MinLevel = 1, MaxLevel = 50, Percent = 400 },
            new PremiumItemExpData { Index = 2, Type = GoldPremium, MinLevel = 51, MaxLevel = 83, Percent = 100 },
        ]);
        data.PremiumItemTable.Returns(new Dictionary<byte, PremiumItemData>
        {
            [GoldPremium] = new() { Type = GoldPremium, ExpRestorePercent = 2.0 },
            [FreeResetPremium] = new() { Type = FreeResetPremium },
        });
        data.GetPremiumProperty(GoldPremium, PremiumPropertyType.Noah).Returns(5);
        data.GetPremiumProperty(GoldPremium, PremiumPropertyType.Drop).Returns(1);
        data.GetPremiumProperty(GoldPremium, PremiumPropertyType.RepairDiscount).Returns(50);
        return data;
    }

    private static UserSession Player(byte premiumType, byte level)
    {
        var sessions = new SessionManager();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessions.CreateSession(client, 1, 1);
        session.Level = level;
        session.PremiumService = premiumType;
        if (premiumType != 0)
            session.HeldPremiums[premiumType] = DateTime.UtcNow.AddDays(3);
        return session;
    }

    [Fact]
    public void TheListCarriesTheBonusesTheServerApplies()
    {
        var bonuses = PremiumBonusList.For(Player(GoldPremium, HighLevel), GameData());

        bonuses.Should().Equal(
            new PremiumBonus(PremiumBonusKind.Experience, 100),
            new PremiumBonus(PremiumBonusKind.Coins, 5),
            new PremiumBonus(PremiumBonusKind.Drops, 1),
            new PremiumBonus(PremiumBonusKind.RepairDiscount, 50),
            new PremiumBonus(PremiumBonusKind.SellPrice, NpcSellRules.PremiumBonusPercent),
            new PremiumBonus(PremiumBonusKind.DeathExpLoss, 2),
            new PremiumBonus(PremiumBonusKind.PriceHistory, 0));
    }

    [Fact]
    public void TheExperienceBonusFollowsTheLevelBand()
    {
        var bonuses = PremiumBonusList.For(Player(GoldPremium, LowLevel), GameData());

        bonuses[0].Should().Be(new PremiumBonus(PremiumBonusKind.Experience, 400));
    }

    [Fact]
    public void FreeResetsAreListedOnlyForTheirPremium()
    {
        var bonuses = PremiumBonusList.For(Player(FreeResetPremium, HighLevel), GameData());

        bonuses.Select(b => b.Kind).Should().Equal(
            PremiumBonusKind.SellPrice, PremiumBonusKind.FreeReset, PremiumBonusKind.FreeRebirthReset, PremiumBonusKind.PriceHistory);
    }

    [Fact]
    public void NoPremiumListsNothing()
    {
        PremiumBonusList.For(Player(0, HighLevel), GameData()).Should().BeEmpty();
    }

    [Fact]
    public void ThePacketIsThe2619ListThenTheBonuses()
    {
        var packet = MiscPacketWriter.Premium([new HeldPremium(GoldPremium, 72)], GoldPremium,
            [new PremiumBonus(PremiumBonusKind.Coins, 5), new PremiumBonus(PremiumBonusKind.PriceHistory, 0)]);
        packet.ResetOffset();

        packet.ReadByte().Should().Be(MiscPacketWriter.PremiumList);
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be(GoldPremium);
        packet.ReadUShort().Should().Be(72);
        packet.ReadByte().Should().Be(GoldPremium);
        packet.ReadByte().Should().Be(2);
        packet.ReadByte().Should().Be((byte)PremiumBonusKind.Coins);
        packet.ReadShort().Should().Be(5);
        packet.ReadByte().Should().Be((byte)PremiumBonusKind.PriceHistory);
        packet.ReadShort().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }
}
