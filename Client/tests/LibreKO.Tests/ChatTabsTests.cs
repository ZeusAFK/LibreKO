using System.Linq;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class ChatTabsTests
{
    [Theory]
    [InlineData(1, false, ChatCategory.General)]
    [InlineData(12, false, ChatCategory.General)]
    [InlineData(2, false, ChatCategory.Whisper)]
    [InlineData(3, false, ChatCategory.Party)]
    [InlineData(4, false, ChatCategory.Party)]
    [InlineData(6, false, ChatCategory.Clan)]
    [InlineData(23, false, ChatCategory.Clan)]
    [InlineData(15, false, ChatCategory.Alliance)]
    [InlineData(5, false, ChatCategory.Shout)]
    [InlineData(14, false, ChatCategory.Trade)]
    [InlineData(13, false, ChatCategory.Commander)]
    [InlineData(1, true, ChatCategory.Notice)]
    [InlineData(8, false, ChatCategory.Notice)]
    public void EveryChatTypeHasOneCategory(byte type, bool serverNotice, ChatCategory expected) =>
        Assert.Equal(expected, ChatCategories.Of(type, serverNotice));

    [Fact]
    public void DefaultTabsShowTheirOwnLinesAndNotices()
    {
        var general = ChatTabs.All[ChatTabs.IndexOf("general")];
        Assert.True(general.Filter.HasFlag(ChatCategory.Shout));
        Assert.True(general.Filter.HasFlag(ChatCategory.Commander));
        Assert.False(general.Filter.HasFlag(ChatCategory.Party));
        Assert.Equal(ChatCategory.All, ChatTabs.All[0].Filter);
        Assert.Equal((byte)3, ChatTabs.All[ChatTabs.IndexOf("party")].SendChannel);
        Assert.Equal((byte)0, ChatTabs.All[ChatTabs.IndexOf("system")].SendChannel);
    }

    [Theory]
    [InlineData("all", "ffffffff")]
    [InlineData("general", "ffffffff")]
    [InlineData("whisper", "80ffffff")]
    [InlineData("party", "00c0c0ff")]
    [InlineData("clan", "00ff00ff")]
    [InlineData("alliance", "ff6b6bff")]
    [InlineData("shout", "f86605ff")]
    [InlineData("trade", "c6c6fbff")]
    [InlineData("system", "ffff00ff")]
    public void EachTabWearsItsChannelsColour(string id, string rgba) =>
        Assert.Equal(rgba, new ChatColors().ForTab(ChatTabs.All[ChatTabs.IndexOf(id)]).ToHtml());

    [Fact]
    public void TheAllTabLeavesTheSendChannelAlone() =>
        Assert.Equal(ChatTabs.NoSendChannel, ChatTabs.All[ChatTabs.IndexOf("all")].SendChannel);

    [Fact]
    public void OnlyPrivateCircleLinesMarkOtherTabs()
    {
        var filters = ChatTabs.All.Select(t => t.Filter).ToList();
        var marked = ChatUnread.TabsToMark(filters, ChatCategory.Party, selected: 0);
        Assert.Contains(ChatTabs.IndexOf("party"), marked);
        Assert.DoesNotContain(0, marked);
        Assert.Empty(ChatUnread.TabsToMark(filters, ChatCategory.Shout, selected: 0));
        Assert.DoesNotContain(ChatTabs.IndexOf("party"), ChatUnread.TabsToMark(filters, ChatCategory.Party, ChatTabs.IndexOf("party")));
    }

    [Fact]
    public void TabsThatDoNotFitGoToTheOverflowAndTheSelectedOneStaysVisible()
    {
        float[] widths = { 40, 60, 60, 60, 60 };
        var fit = ChatTabs.Fit(widths, available: 200, gap: 3, overflowWidth: 26, selected: 4);
        Assert.Equal(3, fit.Visible);
        Assert.Equal(2, fit.SelectedSlot);
        var all = ChatTabs.Fit(widths, available: 400, gap: 3, overflowWidth: 26, selected: 4);
        Assert.Equal(5, all.Visible);
        Assert.Equal(4, all.SelectedSlot);
    }
}
