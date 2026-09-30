using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class NestDungeonTests
{
    private const int Moradon = 21;
    private const int FirstNest = 81;
    private const int JuraidMountain = 87;

    [Theory]
    [InlineData(MonsterStoneResult.PartyLeaderOnly)]
    [InlineData(MonsterStoneResult.Failed)]
    [InlineData(MonsterStoneResult.Refused)]
    [InlineData(MonsterStoneResult.PartyLevelTooHigh)]
    [InlineData(MonsterStoneResult.NotEnoughHealth)]
    public void EveryRefusalTellsThePlayerSomething(MonsterStoneResult result)
    {
        Assert.False(string.IsNullOrWhiteSpace(NestDungeon.Message(result, Moradon)));
    }

    [Fact]
    public void EnteringNeedsNoMessage()
    {
        Assert.Null(NestDungeon.Message(MonsterStoneResult.Entered, Moradon));
    }

    [Theory]
    [InlineData(FirstNest)]
    [InlineData(JuraidMountain)]
    public void AZoneRefusalInsideAnInstanceSaysSo(int zone)
    {
        Assert.NotEqual(
            NestDungeon.Message(MonsterStoneResult.CannotEnterHere, Moradon),
            NestDungeon.Message(MonsterStoneResult.CannotEnterHere, zone));
        Assert.True(NestDungeon.IsInstancedZone(zone));
    }
}
