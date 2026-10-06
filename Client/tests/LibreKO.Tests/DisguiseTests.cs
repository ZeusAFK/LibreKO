using System.Linq;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class DisguiseTests
{
    private const int EventTotem = 379090000;
    private const int Totem = 379093000;
    private const int Scroll = 381001000;

    private const string Table = """
        [
         {"id": 1, "name": "Kecoon bone collector", "level": 1, "skill": 470020, "item": 379090000, "access": 3, "note": "", "classLimit": 0},
         {"id": 3, "name": "Zombie", "level": 20, "skill": 470050, "item": 379090000, "access": 3, "note": "", "classLimit": 0},
         {"id": 101, "name": "Kecoon bone collector", "level": 1, "skill": 471020, "item": 379093000, "access": 1, "note": "", "classLimit": 0},
         {"id": 103, "name": "Zombie", "level": 20, "skill": 471050, "item": 379093000, "access": 1, "note": "", "classLimit": 0},
         {"id": 104, "name": "Lycan", "level": 20, "skill": 471070, "item": 379093000, "access": 1, "note": "", "classLimit": 0},
         {"id": 118, "name": "Orc bowman", "level": 1, "skill": 471310, "item": 379093000, "access": 2, "note": "Only archery log transformation is possible.", "classLimit": 2},
         {"id": 201, "name": "Kecoon bone collector", "level": 1, "skill": 472020, "item": 381001000, "access": 1, "note": "", "classLimit": 0}
        ]
        """;

    private static DisguiseForm[] All => Disguise.Parse(Table);

    [Fact]
    public void EveryRowIsRead()
    {
        var form = All.Single(f => f.Id == 118);
        Assert.Equal(7, All.Length);
        Assert.Equal("Orc bowman", form.Name);
        Assert.Equal(1, form.Level);
        Assert.Equal(471310, form.Skill);
        Assert.Equal(Totem, form.Item);
        Assert.Equal(Disguise.WithoutPremium, form.Access);
        Assert.Equal(2, form.ClassLimit);
    }

    [Fact]
    public void OnlyTheUsedItemsFormsAreListed()
    {
        var forms = Disguise.FormsFor(All, Scroll, premiumType: 0);

        Assert.Equal(new[] { 201 }, forms.Select(f => f.Id));
    }

    [Fact]
    public void PremiumFormsNeedPremiumAndFreeFormsAreHiddenFromPremium()
    {
        Assert.Empty(Disguise.FormsFor(All, EventTotem, premiumType: 0));
        Assert.Equal(new[] { 1, 3 }, Disguise.FormsFor(All, EventTotem, premiumType: 1).Select(f => f.Id));
        Assert.DoesNotContain(118, Disguise.FormsFor(All, Totem, premiumType: 1).Select(f => f.Id));
        Assert.Contains(118, Disguise.FormsFor(All, Totem, premiumType: 0).Select(f => f.Id));
    }

    [Fact]
    public void FormsAreGroupedByTheirMinimumLevel()
    {
        var groups = Disguise.Groups(Disguise.FormsFor(All, Totem, premiumType: 0));

        Assert.Equal(new[] { 1, 20 }, groups.Select(g => g.Level));
        Assert.Equal(new[] { 101, 118 }, groups[0].Forms.Select(f => f.Id));
        Assert.Equal(new[] { 103, 104 }, groups[1].Forms.Select(f => f.Id));
    }

    [Fact]
    public void ATransformationIsRefusedBelowItsLevelOrWhileTransformed()
    {
        var zombie = All.Single(f => f.Id == 103);

        Assert.Equal(DisguiseRefusal.Level, Disguise.Refusal(zombie, level: 19, transformed: false));
        Assert.Equal(DisguiseRefusal.Transformed, Disguise.Refusal(zombie, level: 40, transformed: true));
        Assert.Equal(DisguiseRefusal.None, Disguise.Refusal(zombie, level: 20, transformed: false));
    }
}
