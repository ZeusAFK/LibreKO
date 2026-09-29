using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class SkillAnimationTests
{
    private const int BlowArrow = 108575;
    private const int ElMoradBlowArrow = 208575;
    private const int Stroke = 101001;

    [Theory]
    [InlineData(BlowArrow)]
    [InlineData(ElMoradBlowArrow)]
    public void BlowArrowPlaysItsDaggerStabEvenWhenTheBowGivesNoWeaponAnimation(int skillId)
    {
        Assert.Equal(SkillAnimation.SkillDaggerB, SkillAnimation.WithForced(skillId, SkillAnimation.None));
    }

    [Fact]
    public void OtherSkillsKeepWhatTheirWeaponGives()
    {
        Assert.Equal(SkillAnimation.None, SkillAnimation.WithForced(Stroke, SkillAnimation.None));
        Assert.Equal(112, SkillAnimation.WithForced(Stroke, 112));
    }

    [Fact]
    public void ABowAloneIsNotAMeleeWeapon()
    {
        Assert.Equal(SkillAnimation.None, SkillAnimation.WeaponBucket(WeaponAnimation.NoItem, WeaponAnimation.Bow));
    }
}
