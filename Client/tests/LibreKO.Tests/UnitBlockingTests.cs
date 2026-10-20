using Godot;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class UnitBlockingTests
{
    private static readonly Vector2 Unit = Vector2.Zero;
    private const int Moradon = 21;
    private const int RonarkLand = 71;
    private const int HellAbyss = 33;
    private const int MonsterSuppression = 81;
    private const int DrakisTower = 95;
    private const int BorderDefenseWar = 89;
    private const int Mins = 1501;
    private const int LeatherScarecrow = 19067;
    private const int ThornTrap = 30350;
    private const int Rockslide = 30200;
    private const int NeverBlocking = 32008;

    [Theory]
    [InlineData(Moradon)]
    [InlineData(RonarkLand)]
    public void YouWalkThroughMonstersInOrdinaryZones(int zone) =>
        Assert.False(UnitBlocking.NpcCanBlock(Mins, hostile: true, Nations.Karus, Nations.NotSelected, zone));

    [Theory]
    [InlineData(HellAbyss)]
    [InlineData(MonsterSuppression)]
    [InlineData(DrakisTower)]
    public void HostileMonstersBlockInTheZonesThatSaySo(int zone) =>
        Assert.True(UnitBlocking.NpcCanBlock(Mins, hostile: true, Nations.Karus, Nations.NotSelected, zone));

    [Fact]
    public void AScarecrowDoesNotBlockInMoradon() =>
        Assert.False(UnitBlocking.NpcCanBlock(LeatherScarecrow, hostile: true, Nations.Karus, Nations.NotSelected, Moradon));

    [Fact]
    public void AThornTrapOfTheOtherNationBlocksEverywhere()
    {
        Assert.True(UnitBlocking.NpcCanBlock(ThornTrap, hostile: true, Nations.Karus, Nations.ElMorad, Moradon));
        Assert.False(UnitBlocking.NpcCanBlock(ThornTrap, hostile: true, Nations.Karus, Nations.Karus, Moradon));
    }

    [Fact]
    public void SomeNpcsBlockWithoutBeingHostileOnlyWhereNpcsBlock()
    {
        Assert.True(UnitBlocking.NpcCanBlock(Rockslide, hostile: false, Nations.Karus, Nations.Karus, MonsterSuppression));
        Assert.False(UnitBlocking.NpcCanBlock(Rockslide, hostile: false, Nations.Karus, Nations.Karus, Moradon));
    }

    [Fact]
    public void ANpcThatIsNotHostileOrNeverBlocksDoesNotBlock()
    {
        Assert.False(UnitBlocking.NpcCanBlock(Mins, hostile: false, Nations.Karus, Nations.NotSelected, MonsterSuppression));
        Assert.False(UnitBlocking.NpcCanBlock(NeverBlocking, hostile: true, Nations.Karus, Nations.NotSelected, MonsterSuppression));
    }

    [Fact]
    public void HostilePlayersBlockExceptInTheSecondBorderDefenseWar()
    {
        Assert.True(UnitBlocking.PlayerCanBlock(hostile: true, Moradon));
        Assert.False(UnitBlocking.PlayerCanBlock(hostile: false, Moradon));
        Assert.False(UnitBlocking.PlayerCanBlock(hostile: true, BorderDefenseWar));
    }

    [Fact]
    public void AStepIntoAUnitsSpaceIsBlocked()
    {
        Assert.True(UnitBlocking.Blocks(new Vector2(2f, 0f), new Vector2(1.3f, 0f), Unit, 1.8f, 1.8f));
    }

    [Fact]
    public void SteppingAwayOrPastTheSpaceIsAllowed()
    {
        Assert.False(UnitBlocking.Blocks(new Vector2(1.3f, 0f), new Vector2(1.4f, 0f), Unit, 1.8f, 1.8f));
        Assert.False(UnitBlocking.Blocks(new Vector2(3f, 0f), new Vector2(2f, 0f), Unit, 1.8f, 1.8f));
    }

    [Fact]
    public void TallerUnitsKeepYouFurtherAway()
    {
        Assert.True(UnitBlocking.Blocks(new Vector2(4f, 0f), new Vector2(3.5f, 0f), Unit, 1.8f, 8f));
        Assert.False(UnitBlocking.Blocks(new Vector2(4f, 0f), new Vector2(3.5f, 0f), Unit, 1.8f, 1.8f));
    }
}
