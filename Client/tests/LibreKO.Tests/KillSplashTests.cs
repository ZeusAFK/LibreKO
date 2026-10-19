using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class KillSplashTests
{
    [Theory]
    [InlineData(1, "back_0")]
    [InlineData(5, "back_3")]
    [InlineData(10, "back_4")]
    [InlineData(30, "back_6")]
    [InlineData(17, "back_5")]
    public void TheBackPlateFollowsTheCount(int count, string back)
    {
        Assert.Equal(back, KillSplash.BackFor(count));
    }

    [Theory]
    [InlineData(1, 1, "word_first_kill")]
    [InlineData(4, 1, "word_diamond_kill")]
    [InlineData(10, 2, "word_legend_kill")]
    [InlineData(20, 1, "word_karus_hero")]
    [InlineData(20, 2, "word_elmorad_hero")]
    [InlineData(50, 1, "word_legendary_hero")]
    [InlineData(150, 2, "word_unstoppable")]
    [InlineData(37, 1, "word_legendary")]
    public void TheWordFollowsThe2619Table(int count, int nation, string word)
    {
        Assert.Equal(word, KillSplash.WordFor(count, nation));
    }

    [Fact]
    public void GemsRunUpToElevenThenTheLastOne()
    {
        Assert.Equal("gem_0", KillSplash.GemFor(1));
        Assert.Equal("gem_10", KillSplash.GemFor(11));
        Assert.Equal("gem_12", KillSplash.GemFor(12));
    }

    [Fact]
    public void DigitsCentreOneAndSplitTwoOrThree()
    {
        Assert.Single(KillSplash.Digits(7));
        Assert.Equal([1, 2], KillSplash.Digits(12).ConvertAll(d => d.Digit));
        Assert.Equal([9, 9, 9], KillSplash.Digits(1500).ConvertAll(d => d.Digit));
    }

    [Theory]
    [InlineData(3, 1, 3)]
    [InlineData(20, 1, 12)]
    [InlineData(20, 2, 11)]
    [InlineData(33, 1, 10)]
    [InlineData(50, 1, 0)]
    public void NarrationPicksTheTierFile(int count, int nation, int file)
    {
        Assert.Equal(file, KillSplash.NarrationFor(count, nation));
    }

    [Fact]
    public void TheKillPacketReadsKindIdNameNationAndCount()
    {
        var p = new Packet(GameOpcodes.GS_KILL_ASSIST);
        p.WriteByte((byte)KillAssistSubOpcode.Kill);
        p.WriteByte((byte)KillStreakKind.OwnStreak);
        p.WriteInt(7);
        p.WriteString("Rikka");
        p.WriteByte(1);
        p.WriteUShort(3);
        p.ResetOffset();
        p.ReadByte();

        var kill = KillAssistReplies.ReadKill(p);

        Assert.Equal(new KillStreakReply(KillStreakKind.OwnStreak, 7, "Rikka", 1, 3), kill);
    }
}
