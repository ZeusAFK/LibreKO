using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class PetSkillsTests
{
    private const int KaulClass = 101;
    private const int Slap = 301_001;
    private const int FatalAttack = 301_006;
    private const int PassionOfTheSoul = 301_027;
    private const int LeechKingsManaBurn = 301_011;
    private const int MonsterPoison = 300_101;

    [Fact]
    public void TheBarHoldsTheSharedSkillsInOrderWithoutTheAttackOrder()
    {
        var skills = new[]
        {
            (PassionOfTheSoul, PetSkills.SharedPageFirst),
            (Slap, PetSkills.SharedPageFirst),
            (PetSkills.DesignatedAttack, PetSkills.SharedPageFirst),
            (LeechKingsManaBurn, 10),
            (MonsterPoison, PetSkills.SharedPageFirst),
            (FatalAttack, PetSkills.SharedPageFirst),
        };

        Assert.Equal(new[] { Slap, FatalAttack, PassionOfTheSoul }, PetSkills.BarSkills(skills, KaulClass));
    }

    [Theory]
    [InlineData(Slap, PetSkills.SharedPageFirst, true)]
    [InlineData(Slap, PetSkills.SharedPageLast, true)]
    [InlineData(Slap, KaulClass * PetSkills.ClassPageDivisor, true)]
    [InlineData(Slap, 1020, false)]
    [InlineData(MonsterPoison, PetSkills.SharedPageFirst, false)]
    public void AFamiliarUsesTheSharedPageAndItsOwnClassPage(int skillId, int page, bool belongs) =>
        Assert.Equal(belongs, PetSkills.BelongsTo(skillId, page, KaulClass));

    [Fact]
    public void ASkillUnlocksAtItsLevel()
    {
        Assert.True(PetSkills.Unlocked(10, 10));
        Assert.False(PetSkills.Unlocked(10, 9));
    }
}
