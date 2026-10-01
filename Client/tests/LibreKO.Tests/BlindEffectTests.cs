using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class BlindEffectTests
{
    [Theory]
    [InlineData(BlindEffect.BlindBuffType, BlindMode.Blind)]
    [InlineData(BlindEffect.DisableTargetingBuffType, BlindMode.Unsight)]
    [InlineData(BlindEffect.UnsightBuffType, BlindMode.Unsight)]
    [InlineData(4, BlindMode.None)]
    public void EachSightDebuffPicksItsRetailLayer(int buffType, BlindMode expected)
    {
        Assert.Equal(expected, BlindEffect.ModeFor(buffType));
    }

    [Fact]
    public void TheLayerHoldsForTheDurationThenFadesOut()
    {
        var blind = BlindEffect.Start(BlindEffect.BlindBuffType, now: 10, seconds: 3);

        Assert.Equal(BlindEffect.HoldAlpha, blind.Alpha(10));
        Assert.Equal(BlindEffect.HoldAlpha, blind.Alpha(12.9));
        Assert.Equal(BlindEffect.HoldAlpha / 2, blind.Alpha(13 + BlindEffect.FadeSeconds / 2), 3);
        Assert.Equal(0f, blind.Alpha(13 + BlindEffect.FadeSeconds));
        Assert.Equal(0f, blind.Grey);
    }

    [Fact]
    public void OthersVanishForTheDurationAndReturnWhenTheFadeStarts()
    {
        var blind = BlindEffect.Start(BlindEffect.BlindBuffType, now: 10, seconds: 3);

        Assert.True(blind.HidesOthers(10));
        Assert.True(blind.HidesOthers(12.9));
        Assert.False(blind.HidesOthers(13));
        Assert.False(BlindEffect.Start(4, 10, 3).HidesOthers(11));
        Assert.True(BlindEffect.Start(BlindEffect.UnsightBuffType, 10, 3).HidesOthers(11));
    }

    [Fact]
    public void UnsightIsGreyAndOtherBuffsDrawNothing()
    {
        Assert.Equal(BlindEffect.UnsightGrey, BlindEffect.Start(BlindEffect.UnsightBuffType, 0, 3).Grey);
        Assert.Equal(0f, BlindEffect.Start(4, 0, 3).Alpha(1));
    }

    [Fact]
    public void OnlyTheBuffHalfOfATwoTypeSkillIsItsSecondaryEcho()
    {
        const int blindingStrafeBuff = BlindEffect.BlindBuffType;

        Assert.True(SecondaryBuff.IsEcho(MagicType.Ranged, MagicType.Buff, blindingStrafeBuff,
            new short[] { 0, 1, 0, 3, 0, 0, 0 }));
        Assert.False(SecondaryBuff.IsEcho(MagicType.Ranged, MagicType.Buff, blindingStrafeBuff,
            new short[] { 0, 0, 0, 0, 0, 0, 0 }));
        Assert.False(SecondaryBuff.IsEcho(MagicType.Ranged, MagicType.Buff, blindingStrafeBuff,
            new short[] { 0, 0, 0, -100, 0, 0, 0 }));
        Assert.Equal(3, SecondaryBuff.Seconds(new short[] { 0, 1, 0, 3, 0, 0, 0 }));
    }

    [Fact]
    public void TheSlowRidesInTheSpeedSlotOfTheEcho()
    {
        Assert.Equal(48, SecondaryBuff.SpeedPercent(new short[] { 0, 1, 0, 11, 0, 48, 0 }));
        Assert.Equal(100, SecondaryBuff.SpeedPercent(new short[] { 0, 1, 0 }));
    }

    [Fact]
    public void ASkillWhoseOwnTypeIsTheBuffHasNoSecondaryEcho()
    {
        Assert.False(SecondaryBuff.IsEcho(MagicType.Buff, MagicType.Buff, 5, new short[] { 0, 1, 0, 6, 0, 0, 0 }));
        Assert.False(SecondaryBuff.IsEcho(MagicType.Ranged, 0, 0, new short[] { 0, 1, 0, 6, 0, 0, 0 }));
    }
}
