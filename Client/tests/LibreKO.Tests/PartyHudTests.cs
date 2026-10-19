using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class PartyHudTests
{
    [Theory]
    [InlineData(101, 0)]
    [InlineData(107, 1)]
    [InlineData(110, 2)]
    [InlineData(112, 3)]
    [InlineData(115, 4)]
    [InlineData(205, 5)]
    [InlineData(202, 6)]
    [InlineData(209, 7)]
    [InlineData(204, 8)]
    [InlineData(213, 9)]
    public void EachClassGetsTheRetailPartyIcon(int classCode, int icon) =>
        Assert.Equal(icon, PartyHud.ClassIconIndex(classCode));

    private const int WarriorMaster = 206;
    private const int RogueMaster = 208;
    private const int UnknownClass = 0;

    [Theory]
    [InlineData(50, 100, 0.5f)]
    [InlineData(150, 100, 1f)]
    [InlineData(10, 0, 0f)]
    public void BarsFillByTheShareLeft(int current, int max, float expected) =>
        Assert.Equal(expected, PartyHud.Fill(current, max), 3);

    [Fact]
    public void NoStatusKeepsTheRedBar() =>
        Assert.Equal(PartyHpLook.Normal, PartyHud.HpLook(System.Array.Empty<byte>(), 0));

    [Fact]
    public void PoisonTurnsTheBarPurple() =>
        Assert.Equal(PartyHpLook.Drop, PartyHud.HpLook(new[] { (byte)PartyStatusIcon.Poison }, 0));

    [Fact]
    public void TwoStatusesTakeTurnsEverySecond()
    {
        var statuses = new[] { (byte)PartyStatusIcon.Poison, (byte)PartyStatusIcon.OverTimeDamage };
        Assert.Equal(PartyHpLook.Lasting, PartyHud.HpLook(statuses, 0));
        Assert.Equal(PartyHpLook.Drop, PartyHud.HpLook(statuses, 1));
        Assert.Equal(PartyHpLook.Lasting, PartyHud.HpLook(statuses, 2));
    }

    [Theory]
    [InlineData(WarriorMaster, "system/stat-str")]
    [InlineData(RogueMaster, "system/stat-dex")]
    [InlineData(UnknownClass, "system/user")]
    public void EachClassFamilyHasItsGlyph(int classCode, string icon) =>
        Assert.Equal(icon, PartyHud.ClassIcon(classCode));

    private const int NoOne = -1;
    private const int Member = 7001;
    private const int OtherMember = 7002;
    private const int Monster = 9001;

    [Fact]
    public void ASelectedMemberIsTheHighlightedRow() =>
        Assert.Equal(Member, PartyHud.Highlight(Member, selectedInParty: true, pick: NoOne));

    [Fact]
    public void ASelectedMonsterHighlightsNoRow() =>
        Assert.Equal(NoOne, PartyHud.Highlight(Monster, selectedInParty: false, pick: Member));

    [Fact]
    public void WithNothingSelectedThePickedMemberIsHighlighted() =>
        Assert.Equal(Member, PartyHud.Highlight(NoOne, selectedInParty: false, pick: Member));

    [Fact]
    public void WithNothingSelectedAndNoPickNoRowIsHighlighted() =>
        Assert.Equal(NoOne, PartyHud.Highlight(NoOne, selectedInParty: false, pick: NoOne));

    [Fact]
    public void ThePickHoldsWhileTheSelectionIsUnchanged() =>
        Assert.Equal(Member, PartyHud.KeepPick(Member, selectedId: Member, selectionAtPick: Member));

    [Theory]
    [InlineData(NoOne)]
    [InlineData(Monster)]
    [InlineData(OtherMember)]
    public void ThePickEndsWhenTheSelectionChanges(int selectedNow) =>
        Assert.Equal(NoOne, PartyHud.KeepPick(Member, selectedNow, selectionAtPick: Member));

    [Fact]
    public void AnOutOfViewPickHoldsUntilSomethingIsSelected()
    {
        Assert.Equal(Member, PartyHud.KeepPick(Member, selectedId: NoOne, selectionAtPick: NoOne));
        Assert.Equal(NoOne, PartyHud.KeepPick(Member, selectedId: Monster, selectionAtPick: NoOne));
    }
}
