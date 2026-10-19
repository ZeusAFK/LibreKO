using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class KnightRoyaleTests
{
    [Theory]
    [InlineData(20051, 1)]
    [InlineData(20055, 5)]
    [InlineData(20056, 0)]
    public void GasRegionsMapToAreasOutsideIn(int region, int area) =>
        Assert.Equal(area, KnightRoyale.AreaOf(region));

    [Fact]
    public void AReleasedAreaWarnsOfPoisonAndALaterOneShowsItsNumber()
    {
        Assert.Equal(KnightRoyale.PoisonAreaText, KnightRoyale.AreaCaption(2, 2));
        Assert.Equal(KnightRoyale.AreaText, KnightRoyale.AreaCaption(3, 2));
    }

    [Fact]
    public void TheClientAddsOneGemToTheKillsAndSumsTheTotal()
    {
        var reward = new KnightRoyale.Reward(20, 3, 4, 10);
        Assert.Equal(5, reward.KillGems);
        Assert.Equal(15, reward.TotalGems);
        var lines = KnightRoyale.RewardLines(reward, (_, fallback) => fallback);
        Assert.Equal("You have received 15 Soul Gems.", lines[4]);
    }

    [Fact]
    public void SignUpOpenCarriesCountSecondsAndMode()
    {
        var p = new Packet(GameOpcodes.GS_KNIGHT_ROYALE);
        p.WriteByte((byte)KnightRoyaleSignUpResult.Open);
        p.WriteByte(12);
        p.WriteUShort(300);
        p.WriteByte(KnightRoyale.DuoMode);

        var signUp = KnightRoyaleWire.ReadSignUp(p);

        Assert.Equal(new RoyaleSignUp(KnightRoyaleSignUpResult.Open, 12, 300, KnightRoyale.DuoMode), signUp);
        Assert.Equal(KnightRoyale.DuoTitleText, KnightRoyale.TitleFor(signUp.Mode));
    }

    [Fact]
    public void RefusalsCarryOnlyTheirResult()
    {
        var p = new Packet(GameOpcodes.GS_KNIGHT_ROYALE);
        p.WriteByte((byte)KnightRoyaleSignUpResult.Level);

        var signUp = KnightRoyaleWire.ReadSignUp(p);

        Assert.Equal(0, signUp.Participants);
        Assert.Equal(KnightRoyale.LevelText, KnightRoyale.SignUpText(signUp.Result));
    }

    [Fact]
    public void StatsAndItemsReadTheirFields()
    {
        var stats = new Packet(GameOpcodes.GS_KNIGHT_ROYALE);
        stats.WriteInt(9001);
        stats.WriteByte(3);
        stats.WriteShort(400);
        stats.WriteShort(120);
        stats.WriteShort(900);
        stats.WriteShort(850);
        stats.WriteShort(300);
        stats.WriteShort(280);
        Assert.Equal(new RoyaleStats(9001, 3, 400, 120, 900, 850, 300, 280), KnightRoyaleWire.ReadStats(stats));

        var item = new Packet(GameOpcodes.GS_KNIGHT_ROYALE);
        item.WriteByte(2);
        item.WriteInt(930664000);
        item.WriteShort(3);
        item.WriteShort(1);
        item.WriteByte(0);
        Assert.Equal(new RoyaleItem(2, 930664000, 3, 1, 0), KnightRoyaleWire.ReadItem(item));
    }

    [Fact]
    public void ALootFailureStopsAfterItsCode()
    {
        var p = new Packet(GameOpcodes.GS_KNIGHT_ROYALE);
        p.WriteInt(77);
        p.WriteUShort(KnightRoyaleWire.LootFailed);

        Assert.Equal(new RoyaleLoot(77, KnightRoyaleWire.LootFailed, 0, 0), KnightRoyaleWire.ReadLoot(p));
    }

    [Fact]
    public void TheGasCountdownStopsAfterTheLastArea()
    {
        Assert.False(KnightRoyale.GasEnds(1));
        Assert.False(KnightRoyale.GasEnds(5));
        Assert.True(KnightRoyale.GasEnds(6));
        Assert.Equal("04:05", KnightRoyale.Clock(245));
    }
}
