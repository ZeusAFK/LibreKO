using FluentAssertions;
using LibreKO.Game.World;
using Xunit;

namespace LibreKO.Game.Tests;

public class VitalsRegenTests
{
    private const byte Level = 60;
    private const short MaxHp = 3000;
    private const short MaxMp = 1000;
    private const short MasteredMage = 110;
    private const short MasteredMageElMorad = 210;
    private const short PriestNovice = 211;
    private const short MageNovice = 209;
    private const byte Moradon = 21;

    private static RegenSubject Subject(
        short classId = MasteredMage, short mp = MaxMp / 2, byte zone = Moradon,
        bool sitting = false, bool gm = false, bool snowWar = false, short hp = MaxHp / 2) =>
        new(Level, hp, MaxHp, mp, MaxMp, classId, zone, sitting, gm, snowWar);

    [Fact]
    public void StandingRegeneratesManaOnly()
    {
        var amounts = VitalsRegenCalculator.Calculate(Subject(classId: MageNovice));

        amounts.Hp.Should().Be(0, "retail only restores health while sitting");
        amounts.Mp.Should().Be(27);
    }

    [Fact]
    public void SittingRegeneratesBoth()
    {
        var amounts = VitalsRegenCalculator.Calculate(Subject(classId: MageNovice, sitting: true));

        amounts.Hp.Should().Be(183);
        amounts.Mp.Should().Be(59);
    }

    [Theory]
    [InlineData(MasteredMage)]
    [InlineData(MasteredMageElMorad)]
    public void AMasteredMageLowOnManaGetsTheRegenBonus(short classId)
    {
        var low = VitalsRegenCalculator.Calculate(Subject(classId, mp: MaxMp / 10));
        var full = VitalsRegenCalculator.Calculate(Subject(classId, mp: MaxMp / 2));

        low.Mp.Should().Be(32, "the bonus is 120% of the standing rate");
        full.Mp.Should().Be(27, "the bonus only applies below 30% mana");
    }

    [Theory]
    [InlineData(PriestNovice)]
    [InlineData(MageNovice)]
    public void NoOtherClassGetsTheMageRegenBonus(short classId)
    {
        var amounts = VitalsRegenCalculator.Calculate(Subject(classId, mp: MaxMp / 10));

        amounts.Mp.Should().Be(27, "the mastered mage alone gets it");
    }

    [Fact]
    public void AGameMasterSittingIsRestoredInFull()
    {
        var amounts = VitalsRegenCalculator.Calculate(Subject(sitting: true, gm: true));

        amounts.Hp.Should().Be(MaxHp);
        amounts.Mp.Should().Be(MaxMp);
    }

    [Fact]
    public void TheSnowWarReplacesRegenWithAFlatTrickle()
    {
        var amounts = VitalsRegenCalculator.Calculate(
            Subject(zone: 69, sitting: true, snowWar: true));

        amounts.Hp.Should().Be(5);
        amounts.Mp.Should().Be(0, "the snow war grants no mana at all");
    }

    [Fact]
    public void TheSnowWarZoneRegeneratesNormallyWhileTheWarIsClosed()
    {
        var amounts = VitalsRegenCalculator.Calculate(Subject(zone: 69, sitting: true));

        amounts.Hp.Should().Be(183);
    }

    [Fact]
    public void PrisonGivesAFlatShareOfMaximumManaWhileSitting()
    {
        var amounts = VitalsRegenCalculator.Calculate(Subject(zone: 92, sitting: true));

        amounts.Mp.Should().Be(MaxMp * 5 / 100);
    }

    [Fact]
    public void TheDeadRegenerateNothing()
    {
        VitalsRegenCalculator.Calculate(Subject(hp: 0, sitting: true)).Should().Be(default(RegenAmounts));
    }
}
