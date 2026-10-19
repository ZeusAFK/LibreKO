using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class AchievementTitleAnswerTests
{
    [Fact]
    public void ASelectAnswerSkipsTheMainIdAndReadsTheTitleAndResult()
    {
        var p = new Packet(GameOpcodes.GS_ACHIEVEMENT);
        p.WriteUShort(0);
        p.WriteUShort(42);
        p.WriteShort((short)AchievementTitleResult.Done);
        p.ResetOffset();

        var answer = AchievementTitleAnswer.Read(AchievementSubOpcode.SelectDisplayTitle, p);

        Assert.Equal(new AchievementTitleAnswer(AchievementSubOpcode.SelectDisplayTitle, false, 42, AchievementTitleResult.Done, 0), answer);
    }

    [Fact]
    public void ACooldownCarriesItsSeconds()
    {
        var select = new Packet(GameOpcodes.GS_ACHIEVEMENT);
        select.WriteUShort(0);
        select.WriteUShort(42);
        select.WriteShort((short)AchievementTitleResult.SelectCooldown);
        select.WriteUShort(30);
        select.ResetOffset();
        var reset = new Packet(GameOpcodes.GS_ACHIEVEMENT);
        reset.WriteShort((short)AchievementTitleResult.ResetCooldown);
        reset.WriteUShort(12);
        reset.ResetOffset();

        Assert.Equal(30, AchievementTitleAnswer.Read(AchievementSubOpcode.SelectSkillTitle, select)?.Seconds);
        Assert.True(AchievementTitleAnswer.Read(AchievementSubOpcode.ResetDisplayTitle, reset)?.Cooldown);
    }

    [Fact]
    public void AResetAnswerIsOnlyTheResult()
    {
        var p = new Packet(GameOpcodes.GS_ACHIEVEMENT);
        p.WriteShort((short)AchievementTitleResult.Done);
        p.ResetOffset();

        var answer = AchievementTitleAnswer.Read(AchievementSubOpcode.ResetDisplayTitle, p);

        Assert.True(answer?.Reset);
        Assert.Equal(AchievementTitleResult.Done, answer?.Result);
    }
}
