using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class ClassSkillTests
{
    private const int TreesPerClass = 10;
    private const int BasicTab = 0;
    private const int Level = 83;
    private const byte PointsPerTree = 80;

    private const int KarusBerserker = 105;
    private const int KarusGuardian = 106;
    private const int KarusRogue = 102;
    private const int KarusHunter = 107;
    private const int KarusPenetrator = 108;
    private const int ElMoradWizard = 203;
    private const int ElMoradMage = 209;
    private const int ElMoradEnchanter = 210;
    private const int ElMoradPriest = 204;
    private const int ElMoradCleric = 211;
    private const int ElMoradDruid = 212;
    private const int KarusKurian = 113;
    private const int KarusKurianNovice = 114;
    private const int KarusKurianMaster = 115;
    private const int ElMoradAssassin = 208;

    private const int StrokeIndex = 1;
    private const int StrokeLevel = 1;
    private const int PassionTab = 7;
    private const int GainIndex = 705;
    private const int GainLevel = 5;
    private const int SearchTab = 7;
    private const int EvadeIndex = 710;
    private const int EvadeLevel = 10;
    private const int SafetyIndex = 730;
    private const int SafetyLevel = 30;
    private const int FireTab = 5;
    private const int EndureFireIndex = 524;
    private const int EndureFireLevel = 24;
    private const int AuraTab = 6;
    private const int InsensibilitySkinIndex = 603;
    private const int InsensibilitySkinLevel = 3;
    private const int DevilTab = 7;
    private const int DevilTransformationIndex = 710;
    private const int DevilTransformationLevel = 10;

    private const int PetAttack = 301000;
    private const int RiceCake = 501001;
    private const int AttackScroll = 510034;
    private const int SpeedPotion = 610035;
    private const int UnknownClass = 0;

    private static SelfSession Character(int classCode)
    {
        var self = new SelfSession();
        self.ApplyMyInfo(new MyInfo { Class = classCode }, new ItemSlot[InventoryConstants.InventoryTotal]);
        self.Sheet.SeedProgress(Level, 0, 1);
        var points = new byte[MasteryPoints.SlotCount];
        for (int tree = MasteryPoints.FirstTree; tree <= MasteryPoints.LastTree; tree++)
            points[tree] = PointsPerTree;
        self.Mastery.Seed(points);
        return self;
    }

    private static SkillData.Skill Row(int classCode, int tab, int index, int level) => new()
    {
        Id = SkillData.ClassSkillId(classCode, index),
        Tree = classCode * TreesPerClass + tab,
        Level = level,
    };

    [Fact]
    public void AWarriorUsesOnlyTheRowsOfTheClassItHasNow()
    {
        var guardian = Character(KarusGuardian);
        Assert.True(guardian.SkillLearned(Row(KarusGuardian, PassionTab, GainIndex, GainLevel)));
        Assert.False(guardian.SkillLearned(Row(KarusBerserker, PassionTab, GainIndex, GainLevel)));
        Assert.False(guardian.SkillLearned(Row(KarusBerserker, BasicTab, StrokeIndex, StrokeLevel)));
        Assert.True(Character(KarusBerserker).SkillLearned(Row(KarusBerserker, PassionTab, GainIndex, GainLevel)));
    }

    [Fact]
    public void APenetratorNoLongerCastsTheHuntersEvadeAndSafety()
    {
        var penetrator = Character(KarusPenetrator);
        Assert.False(penetrator.SkillLearned(Row(KarusHunter, SearchTab, EvadeIndex, EvadeLevel)));
        Assert.False(penetrator.SkillLearned(Row(KarusHunter, SearchTab, SafetyIndex, SafetyLevel)));
        Assert.True(penetrator.SkillLearned(Row(KarusPenetrator, SearchTab, EvadeIndex, EvadeLevel)));
        Assert.True(penetrator.SkillLearned(Row(KarusPenetrator, SearchTab, SafetyIndex, SafetyLevel)));
        Assert.False(Character(KarusHunter).SkillLearned(Row(KarusRogue, BasicTab, StrokeIndex, StrokeLevel)));
    }

    [Fact]
    public void AMageUsesOnlyTheRowsOfTheClassItHasNow()
    {
        var enchanter = Character(ElMoradEnchanter);
        Assert.True(enchanter.SkillLearned(Row(ElMoradEnchanter, FireTab, EndureFireIndex, EndureFireLevel)));
        Assert.False(enchanter.SkillLearned(Row(ElMoradMage, FireTab, EndureFireIndex, EndureFireLevel)));
        Assert.False(Character(ElMoradMage).SkillLearned(Row(ElMoradWizard, BasicTab, StrokeIndex, StrokeLevel)));
    }

    [Fact]
    public void APriestUsesOnlyTheRowsOfTheClassItHasNow()
    {
        var druid = Character(ElMoradDruid);
        Assert.True(druid.SkillLearned(Row(ElMoradDruid, AuraTab, InsensibilitySkinIndex, InsensibilitySkinLevel)));
        Assert.False(druid.SkillLearned(Row(ElMoradCleric, AuraTab, InsensibilitySkinIndex, InsensibilitySkinLevel)));
        Assert.False(Character(ElMoradCleric).SkillLearned(Row(ElMoradPriest, BasicTab, StrokeIndex, StrokeLevel)));
    }

    [Fact]
    public void AKurianUsesOnlyTheRowsOfTheClassItHasNow()
    {
        var master = Character(KarusKurianMaster);
        Assert.True(master.SkillLearned(Row(KarusKurianMaster, DevilTab, DevilTransformationIndex, DevilTransformationLevel)));
        Assert.False(master.SkillLearned(Row(KarusKurianNovice, DevilTab, DevilTransformationIndex, DevilTransformationLevel)));
        Assert.False(Character(KarusKurianNovice).SkillLearned(Row(KarusKurian, BasicTab, StrokeIndex, StrokeLevel)));
    }

    [Fact]
    public void ARowOfAnotherNationsClassIsAnotherClassSkill()
    {
        Assert.True(SkillData.IsOtherClassSkill(SkillData.ClassSkillId(ElMoradAssassin, SafetyIndex), KarusPenetrator));
        Assert.False(SkillData.IsOtherClassSkill(SkillData.ClassSkillId(KarusPenetrator, SafetyIndex), KarusPenetrator));
    }

    [Theory]
    [InlineData(PetAttack)]
    [InlineData(RiceCake)]
    [InlineData(AttackScroll)]
    [InlineData(SpeedPotion)]
    public void MonsterAndItemRowsBelongToNoClass(int skillId) =>
        Assert.False(SkillData.IsOtherClassSkill(skillId, KarusPenetrator));

    [Fact]
    public void NothingIsAnotherClassSkillBeforeTheClassIsKnown() =>
        Assert.False(SkillData.IsOtherClassSkill(SkillData.ClassSkillId(KarusHunter, SafetyIndex), UnknownClass));

    [Fact]
    public void APromotionIsAnnouncedOnceAndARepeatIsNot()
    {
        var self = Character(KarusHunter);
        int announced = 0;
        self.ClassChanged += () => announced++;

        self.ApplyClassChange(KarusPenetrator);
        self.ApplyClassChange(KarusPenetrator);

        Assert.Equal(KarusPenetrator, self.Class);
        Assert.Equal(1, announced);
    }
}
