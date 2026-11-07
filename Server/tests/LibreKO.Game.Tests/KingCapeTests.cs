using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol;

namespace LibreKO.Game.Tests;

public class KingCapeTests
{
    private const short ClanCape = 12;
    private const byte Red = 200;
    private const byte Green = 100;
    private const byte Blue = 50;

    private static KnightsEntity Clan() => new() { Cape = ClanCape, CapeR = Red, CapeG = Green, CapeB = Blue };

    [Theory]
    [InlineData(AccountNation.Karus, KingSystemRuntimeService.KarusKingCape)]
    [InlineData(AccountNation.ElMorad, KingSystemRuntimeService.ElmoradKingCape)]
    public void AKingInAClanWearsHisNationsKingCapeUndyed(AccountNation nation, ushort kingCape) =>
        KingSystemRuntimeService.CapeOf(king: true, nation, Clan())
            .Should().Be(new KingSystemRuntimeService.WornCape((short)kingCape, 0, 0, 0));

    [Fact]
    public void AMemberWearsTheDyedClanCape() =>
        KingSystemRuntimeService.CapeOf(king: false, AccountNation.Karus, Clan())
            .Should().Be(new KingSystemRuntimeService.WornCape(ClanCape, Red, Green, Blue));

    [Fact]
    public void AMemberOfAClanNotLoadedWearsNoCape() =>
        KingSystemRuntimeService.CapeOf(king: false, AccountNation.Karus, null)
            .Should().Be(new KingSystemRuntimeService.WornCape(0, 0, 0, 0));
}
