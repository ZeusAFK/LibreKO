using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class EventTitlesTests
{
    [Fact]
    public void AShownTitleCarriesItsMessage()
    {
        var p = new Packet(GameOpcodes.GS_NOTICE);
        p.WriteByte((byte)EventTitleSubOpcode.Shown);
        p.WriteString("Exp Event");
        p.WriteString("Earn 50 percent extra EXP.");
        p.ResetOffset();

        Assert.Equal(new EventTitleNotice(false, "Exp Event", "Earn 50 percent extra EXP."), EventTitleNotice.Read(p));
    }

    [Fact]
    public void ARemovalNamesOnlyTheTitle()
    {
        var p = new Packet(GameOpcodes.GS_NOTICE);
        p.WriteByte((byte)EventTitleSubOpcode.Removed);
        p.WriteString("Exp Event");
        p.ResetOffset();

        Assert.Equal(new EventTitleNotice(true, "Exp Event", ""), EventTitleNotice.Read(p));
    }

    [Fact]
    public void TheEffectVariantIsShownAndItsKindByteConsumed()
    {
        var p = new Packet(GameOpcodes.GS_NOTICE);
        p.WriteByte((byte)EventTitleSubOpcode.ShownWithEffect);
        p.WriteString("Flash");
        p.WriteString("EXP +10%");
        p.WriteByte(2);
        p.ResetOffset();

        Assert.Equal(new EventTitleNotice(false, "Flash", "EXP +10%"), EventTitleNotice.Read(p));
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void ASecondShowOfTheSameTitleReplacesItsMessageInPlace()
    {
        var titles = new EventTitles();
        titles.Show("Exp Event", "10");
        titles.Show("Clan Notice", "Siege");
        titles.Show("Exp Event", "50");

        Assert.Equal(new[] { new EventTitle("Exp Event", "50"), new EventTitle("Clan Notice", "Siege") }, titles.Entries);
    }

    [Fact]
    public void CollapsedShowsTheFirstTitleAndTheToggleOnlyWhenThereAreMore()
    {
        var titles = new EventTitles();
        titles.Show("Exp Event", "");
        Assert.Equal(1, titles.ShownCount);
        Assert.False(titles.HasToggle);

        titles.Show("Drop Event", "");
        Assert.Equal(1, titles.ShownCount);
        Assert.True(titles.HasToggle);

        titles.Expand();
        Assert.Equal(2, titles.ShownCount);

        Assert.True(titles.Remove("Exp Event"));
        Assert.False(titles.Remove("Exp Event"));
        Assert.Equal(1, titles.ShownCount);
    }
}
