using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class ChatNoticesTests
{
    [Fact]
    public void AMonumentCaptureCarriesTheNationAndTheName()
    {
        var p = new Packet(GameOpcodes.GS_CHAT);
        p.WriteByte((byte)MonumentNoticeKind.MonumentCaptured);
        p.WriteByte(Nations.Karus);
        p.WriteString("Zeus");
        p.ResetOffset();

        var notice = MonumentNotice.Read(p);

        Assert.NotNull(notice);
        Assert.Equal(Nations.Karus, notice.Value.Nation);
        Assert.Equal("Zeus", notice.Value.Name);
    }

    [Fact]
    public void ASpyRescueReadsItsDoneFlag()
    {
        var p = new Packet(GameOpcodes.GS_CHAT);
        p.WriteByte((byte)MonumentNoticeKind.SpyRescue);
        p.WriteByte(Nations.ElMorad);
        p.WriteString("Rikka");
        p.WriteByte(1);
        p.ResetOffset();

        Assert.True(MonumentNotice.Read(p)?.Done);
    }

    [Fact]
    public void AFailedQuestCarriesItsId()
    {
        var p = new Packet(GameOpcodes.GS_CHAT);
        p.WriteByte((byte)MonumentNoticeKind.QuestFailed);
        p.WriteInt(1234);
        p.ResetOffset();

        Assert.Equal(1234, MonumentNotice.Read(p)?.Value);
    }

    [Fact]
    public void TheChaosStoneNoticeIsTheBreakerThenTheNation()
    {
        var p = new Packet(GameOpcodes.GS_CHAT);
        p.WriteString("Zeus");
        p.WriteByte(Nations.ElMorad);
        p.ResetOffset();

        Assert.Equal(new ChaosStoneNotice("Zeus", Nations.ElMorad), ChaosStoneNotice.Read(p));
    }

    [Fact]
    public void TheBoxNoticeIsTheNameThenTheBoxThenTheItem()
    {
        var p = new Packet(GameOpcodes.GS_CHAT);
        p.WriteSByteString("Zeus");
        p.WriteInt(810_000_000);
        p.WriteInt(389_010_000);
        p.ResetOffset();

        Assert.Equal(new BoxItemNotice("Zeus", 810_000_000, 389_010_000), BoxItemNotice.Read(p));
    }

    [Fact]
    public void AFlashBlinksOnForItsTimeThenOffForASecond()
    {
        var flash = OrderMessage.Flash;
        Assert.True(flash.VisibleAt(0f));
        Assert.True(flash.VisibleAt(1.9f));
        Assert.False(flash.VisibleAt(2.5f));
        Assert.True(flash.VisibleAt(3.1f));
        Assert.True(flash.VisibleAt(6.5f));
        Assert.False(flash.VisibleAt(9f));
    }

    [Fact]
    public void AHoldStaysOnceForItsTime()
    {
        Assert.True(OrderMessage.Hold.VisibleAt(2.9f));
        Assert.False(OrderMessage.Hold.VisibleAt(3f));
    }

    [Fact]
    public void ATooWideLineSplitsAtItsMiddleCharacter()
    {
        Assert.Equal("abcdef\nhijklm", OrderMessage.Split("abcdefghijklm", tooWide: true));
        Assert.Equal("short line", OrderMessage.Split("short line", tooWide: true));
        Assert.Equal("abcdefghijklm", OrderMessage.Split("abcdefghijklm", tooWide: false));
    }
}
