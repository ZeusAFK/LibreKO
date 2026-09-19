using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class AccountPremiumTests
{
    private const byte PlatinumPremium = 12;
    private const int Level = 60;
    private const long MaxExpForLevel = 2_000_000;

    private static IGameDataService GameData(double expRestorePercent)
    {
        var data = Substitute.For<IGameDataService>();
        data.GetMaxExpForLevel(Level).Returns(MaxExpForLevel);
        data.PremiumItemTable.Returns(new Dictionary<byte, PremiumItemData>
        {
            [PlatinumPremium] = new() { Type = PlatinumPremium, ExpRestorePercent = expRestorePercent },
        });
        return data;
    }

    private static UserSession Victim(byte premiumType, double hoursLeft = 24)
    {
        var sessions = new SessionManager();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessions.CreateSession(client, 1, 1);
        session.Level = Level;
        session.ZoneId = (byte)ZoneId.RonarkLand;
        session.Nation = AccountNation.Karus;
        session.PremiumService = premiumType;
        session.PremiumExpiry = DateTime.UtcNow.AddHours(hoursLeft);
        return session;
    }

    [Fact]
    public void PaidUpPremiumNamesItsType()
    {
        var account = new Account
        {
            PremiumType = PlatinumPremium,
            PremiumDate = DateTime.UtcNow.AddDays(30),
        };

        account.RemainingPremiumHours.Should().BeGreaterThan(0);
        account.ActivePremiumType.Should().Be(PlatinumPremium);
    }

    [Fact]
    public void AnExpiredOrAbsentDateLeavesNoPremiumType()
    {
        new Account { PremiumType = PlatinumPremium, PremiumDate = DateTime.UtcNow.AddHours(-1) }
            .ActivePremiumType.Should().Be(0, "the time ran out even though the type is still stored");

        new Account { PremiumType = PlatinumPremium, PremiumDate = null }
            .ActivePremiumType.Should().Be(0);

        new Account { PremiumDate = DateTime.UtcNow.AddDays(30) }
            .ActivePremiumType.Should().Be(0, "premium time with no type buys nothing");
    }

    [Fact]
    public void APremiumTypeIsWhatMakesTheBonusesApply()
    {
        var gameData = GameData(expRestorePercent: 25);

        var withPremium = DeathPenaltyCalculator.CalculatePlayerDeathExpLoss(
            Victim(PlatinumPremium), gameData);
        var without = DeathPenaltyCalculator.CalculatePlayerDeathExpLoss(Victim(0), gameData);

        without.Should().Be(MaxExpForLevel / 20);
        withPremium.Should().Be(without / 4, "a 25% exp-restore premium keeps three quarters back");
    }

    [Fact]
    public void OnlyOnePremiumTypeWaivesAResetCost()
    {
        var free = Victim(PlatinumPremium);
        var paying = Victim(PlatinumPremium + 1);

        CharacterDevelopmentPacketCoordinator.IsResetFree(free).Should().BeTrue(
            "only premium type 12 waives the rebirth reset cost");
        CharacterDevelopmentPacketCoordinator.IsResetFree(paying).Should().BeFalse();
        CharacterDevelopmentPacketCoordinator.IsResetFree(Victim(0)).Should().BeFalse();
    }

    [Fact]
    public void PremiumStopsTheMomentItExpiresWithoutWaitingForALogout()
    {
        var session = Victim(PlatinumPremium);
        session.PremiumType.Should().Be(PlatinumPremium);
        CharacterDevelopmentPacketCoordinator.IsResetFree(session).Should().BeTrue();

        session.PremiumExpiry = DateTime.UtcNow.AddSeconds(-1);

        session.PremiumTime.Should().Be(0);
        session.PremiumType.Should().Be(0, "every premium branch is gated on the time remaining");
        CharacterDevelopmentPacketCoordinator.IsResetFree(session).Should().BeFalse();
        DeathPenaltyCalculator.CalculatePlayerDeathExpLoss(session, GameData(expRestorePercent: 25))
            .Should().Be(MaxExpForLevel / 20, "the exp-restore bonus lapses with the premium");
    }

    [Fact]
    public void ATypeTheTableDoesNotKnowChangesNothing()
    {
        var loss = DeathPenaltyCalculator.CalculatePlayerDeathExpLoss(
            Victim(PlatinumPremium + 1), GameData(expRestorePercent: 25));

        loss.Should().Be(MaxExpForLevel / 20);
    }
}
