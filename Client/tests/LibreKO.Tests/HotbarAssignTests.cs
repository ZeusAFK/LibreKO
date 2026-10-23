using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class HotbarAssignTests
{
    private const int Endure = 105620;
    private const int Scream = 106802;
    private const int Awakening = 115802;
    private const int HpPotion = 389010000;
    private const int KarusPenetrator = 108;
    private const int HunterEvade = 107710;
    private const int PenetratorEvade = 108710;
    private const int UnknownClass = 0;

    [Theory]
    [InlineData(MagicType.None, 0, false)]
    [InlineData(MagicType.None, 1, false)]
    [InlineData(MagicType.None, HotbarAssign.NoSecondType, true)]
    [InlineData(MagicType.Special, 0, true)]
    [InlineData(MagicType.Special, HotbarAssign.NoSecondType, false)]
    [InlineData(MagicType.Melee, 0, true)]
    [InlineData(MagicType.Buff, MagicType.Stealth, true)]
    public void DragFollowsTheSkillTypes(int type1, int type2, bool expected) =>
        Assert.Equal(expected, HotbarAssign.CanDrag(type1, type2));

    [Theory]
    [InlineData(Endure, MagicType.None, false)]
    [InlineData(HotbarAssign.Shouting, MagicType.None, true)]
    [InlineData(HotbarAssign.Assault, MagicType.None, true)]
    [InlineData(HotbarAssign.ElmoradShouting, MagicType.None, true)]
    [InlineData(HotbarAssign.ElmoradAssault, MagicType.None, true)]
    [InlineData(Endure, MagicType.Buff, true)]
    public void AddingRefusesPassivesExceptTheWarShoutsOfBothNations(int skillId, int type1, bool expected) =>
        Assert.Equal(expected, HotbarAssign.CanAdd(skillId, type1));

    [Theory]
    [InlineData(Awakening, MagicType.None, MagicType.None, false)]
    [InlineData(Endure, MagicType.None, MagicType.Melee, false)]
    [InlineData(Scream, MagicType.Melee, MagicType.Buff, true)]
    [InlineData(HotbarAssign.ElmoradShouting, MagicType.None, MagicType.None, true)]
    [InlineData(Awakening, MagicType.None, HotbarAssign.NoSecondType, true)]
    public void TheBarKeepsWhatEitherWayOfPlacingTakes(int skillId, int type1, int type2, bool expected) =>
        Assert.Equal(expected, HotbarAssign.Holds(skillId, type1, type2));

    [Theory]
    [InlineData(PenetratorEvade, KarusPenetrator, true)]
    [InlineData(HunterEvade, KarusPenetrator, false)]
    [InlineData(HotbarAssign.Shouting, KarusPenetrator, true)]
    [InlineData(HunterEvade, UnknownClass, true)]
    public void TheBarKeepsNoSkillOfAnotherClass(int skillId, int classCode, bool expected) =>
        Assert.Equal(expected, HotbarAssign.Holds(skillId, MagicType.Buff, HotbarAssign.NoSecondType, classCode));

    [Fact]
    public void ClearingASavedBarRemovesOnlyTheRefusedEntries()
    {
        var types = new Dictionary<int, (int Type1, int Type2)>
        {
            [Scream] = (MagicType.Melee, MagicType.Buff),
            [Awakening] = (MagicType.None, MagicType.None),
            [Endure] = (MagicType.None, MagicType.None),
            [HotbarAssign.ElmoradShouting] = (MagicType.None, MagicType.None),
        };
        bool Keeps(int id) => !types.TryGetValue(id, out var t) || HotbarAssign.Holds(id, t.Type1, t.Type2);
        int[] bar = { Scream, Awakening, 0, HpPotion, Endure, HotbarAssign.ElmoradShouting };

        Assert.True(HotbarAssign.ClearRefused(bar, Keeps));
        Assert.Equal(new[] { Scream, 0, 0, HpPotion, 0, HotbarAssign.ElmoradShouting }, bar);
        Assert.False(HotbarAssign.ClearRefused(bar, Keeps));
    }
}
