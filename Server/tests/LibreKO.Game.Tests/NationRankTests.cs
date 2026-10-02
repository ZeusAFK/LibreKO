using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class NationRankTests : GameTestBase
{
    private const byte Unranked = NationRanks.Unranked;

    [Theory]
    [InlineData(3, 7, 3, Unranked)]
    [InlineData(9, 2, Unranked, 2)]
    [InlineData(4, 4, 4, 4)]
    [InlineData(Unranked, 1, Unranked, 1)]
    [InlineData(Unranked, Unranked, Unranked, Unranked)]
    public void OnlyTheBetterPlaceIsShown(byte knights, byte personal, byte shownKnights, byte shownPersonal) =>
        new NationRanks(knights, personal).Shown.Should().Be(new NationRanks(shownKnights, shownPersonal));

    [Fact]
    public async Task PlacesFollowTotalAndMonthlyPointsWithinEachNationAndSkipGameMasters()
    {
        using var provider = CreateProvider(db =>
        {
            db.Accounts.AddRange(
                new Account { Id = 1, Login = "a", Password = "x", Nation = AccountNation.Karus },
                new Account { Id = 2, Login = "b", Password = "x", Nation = AccountNation.Karus },
                new Account { Id = 3, Login = "c", Password = "x", Nation = AccountNation.Karus, Authority = AccountAuthority.GameMaster },
                new Account { Id = 4, Login = "d", Password = "x", Nation = AccountNation.ElMorad },
                new Account { Id = 5, Login = "e", Password = "x", Nation = AccountNation.Karus });
            db.Characters.AddRange(
                new Character { AccountId = 1, Name = "Veteran", Loyalty = 90_000, LoyaltyMonthly = 100 },
                new Character { AccountId = 2, Name = "Rising", Loyalty = 10_000, LoyaltyMonthly = 5_000 },
                new Character { AccountId = 3, Name = "Warden", Loyalty = 900_000, LoyaltyMonthly = 90_000 },
                new Character { AccountId = 4, Name = "Aurelia", Loyalty = 1_000, LoyaltyMonthly = 50 },
                new Character { AccountId = 5, Name = "Fresh", Loyalty = 0, LoyaltyMonthly = 0 });
        });

        var ranks = provider.GetRequiredService<INationRankService>();
        await ranks.RefreshAsync();

        ranks.Of(await GetCharacterIdAsync(provider, "Veteran")).Should().Be(new NationRanks(1, 2));
        ranks.Of(await GetCharacterIdAsync(provider, "Rising")).Should().Be(new NationRanks(2, 1));
        ranks.Of(await GetCharacterIdAsync(provider, "Aurelia")).Should().Be(new NationRanks(1, 1));
        ranks.Of(await GetCharacterIdAsync(provider, "Warden")).Should().Be(NationRanks.None);
        ranks.Of(await GetCharacterIdAsync(provider, "Fresh")).Should().Be(NationRanks.None);
    }

    [Fact]
    public async Task AnOnlinePlayerTakesTheNewPlacesAtTheRefresh()
    {
        using var provider = CreateProvider(db =>
        {
            db.Accounts.Add(new Account { Id = 1, Login = "a", Password = "x", Nation = AccountNation.Karus });
            db.Characters.Add(new Character { AccountId = 1, Name = "Veteran", Loyalty = 90_000, LoyaltyMonthly = 100 });
        });
        var client = Substitute.For<IClient>();
        client.Id.Returns(System.Guid.NewGuid());
        var session = provider.GetRequiredService<SessionManager>()
            .CreateSession(client, await GetCharacterIdAsync(provider, "Veteran"), accountId: 1);

        await provider.GetRequiredService<INationRankService>().RefreshAsync();

        session.NationRanks.Should().Be(new NationRanks(1, 1));
    }

    [Fact]
    public void TheUserRecordCarriesTheShownPlacesAfterTheDirection()
    {
        var visuals = Enumerable.Range(0, 17)
            .Select(_ => new UserInfoPacketWriter.VisualItem(0, 0, 0))
            .ToArray();

        var packet = new Packet(GameOpcodes.GS_USER_INOUT);
        UserInfoPacketWriter.WriteRecord(packet, new UserInfoPacketWriter.UserState(
            Name: "Aurelia", Nation: 1, KnightsId: 0, KnightsFame: 0, Clan: null,
            NoClanNationCode: 93, Level: 62, Race: 1, Class: 105,
            X: 5430, Z: 3770, Y: 120, Face: 2, Hair: 7,
            Pose: (byte)UserPoseState.Standing,
            NeedParty: false, IsGameMaster: false, IsPartyLeader: false, Invisibility: 0,
            Direction: 90, ZoneId: 21, IsHidingHelmet: false, DisplayTitleId: 0, Visuals: visuals,
            Ranks: new NationRanks(12, 3)));

        packet.ResetOffset();
        packet.ReadSByteString();
        packet.ReadBytes(1 + 3 + 2 + 1 + 14 + 1 + 1 + 2 + 2 + 2 + 2 + 1 + 4);
        packet.ReadBytes(1 + 1 + 4 + 1 + 1 + 1 + 1 + 3);
        packet.ReadShort().Should().Be(90);
        packet.ReadBytes(1 + 1 + 2);

        packet.ReadByte().Should().Be(Unranked);
        packet.ReadByte().Should().Be(3);
    }
}
