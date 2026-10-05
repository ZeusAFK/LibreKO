using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class ChatPrefsTests
{
    [Fact]
    public void EverySettingSurvivesSaving()
    {
        var prefs = new ChatPrefs { Tab = "clan", Timestamps = true, FontSize = 2, Background = 3, Locked = true, NearbyShown = false };
        var party = ChatTabs.All[ChatTabs.IndexOf("party")];
        prefs.SetFilter(party, ChatCategory.Party | ChatCategory.Shout);

        var loaded = ChatPrefs.Parse(prefs.Format());

        Assert.Equal("clan", loaded.Tab);
        Assert.True(loaded.Timestamps);
        Assert.Equal(2, loaded.FontSize);
        Assert.Equal(3, loaded.Background);
        Assert.True(loaded.Locked);
        Assert.False(loaded.NearbyShown);
        Assert.Equal(ChatCategory.Party | ChatCategory.Shout, loaded.Filter(party));
    }

    [Fact]
    public void UntouchedTabsUseTheirDefaultFilterAndResetForgetsChanges()
    {
        var prefs = new ChatPrefs();
        var clan = ChatTabs.All[ChatTabs.IndexOf("clan")];
        Assert.Equal(clan.Filter, prefs.Filter(clan));
        prefs.SetFilter(clan, ChatCategory.Clan);
        prefs.ResetFilters();
        Assert.Equal(clan.Filter, prefs.Filter(clan));
    }

    [Fact]
    public void ABrokenSaveGivesTheDefaults()
    {
        var prefs = ChatPrefs.Parse("font=9;bg=-2;garbage;tab=");
        Assert.Equal(ChatPrefs.DefaultFontSize, prefs.FontSize);
        Assert.Equal(ChatPrefs.DefaultBackground, prefs.Background);
        Assert.Equal("all", prefs.Tab);
        Assert.True(prefs.NearbyShown);
    }
}
