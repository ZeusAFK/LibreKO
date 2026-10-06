using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class GiftRecipientsTests
{
    private static GiftContact Friend(string name, bool online = false) => new(name, 60, 105, online, GiftContactSource.Friend);
    private static GiftContact Clan(string name, bool online = false) => new(name, 70, 211, online, GiftContactSource.Clan);

    private static string[] Names(IEnumerable<GiftContact> contacts) => contacts.Select(c => c.Name).ToArray();

    [Fact]
    public void AFriendWhoIsAlsoInTheClanIsListedOnceWithBothSources()
    {
        var merged = GiftRecipients.Merge([Friend("Rikka"), Clan("rikka", online: true)]);
        var contact = Assert.Single(merged);
        Assert.Equal(GiftContactSource.Friend | GiftContactSource.Clan, contact.Source);
        Assert.True(contact.Online);
    }

    [Fact]
    public void ExactMatchesComeFirst_ThenNamesStartingWithTheText_ThenTheRest()
    {
        var contacts = new[] { Friend("Marika"), Friend("Rikkard"), Friend("Rikka") };
        Assert.Equal(["Rikka", "Rikkard", "Marika"], Names(GiftRecipients.Suggest(contacts, "rik", "Zeus")));
    }

    [Fact]
    public void WithinARankOnlineContactsComeFirst()
    {
        var contacts = new[] { Friend("Alpha"), Clan("Beta", online: true) };
        Assert.Equal(["Beta", "Alpha"], Names(GiftRecipients.Suggest(contacts, "", "Zeus")));
    }

    [Fact]
    public void TheBuyerIsNeverSuggested() =>
        Assert.Equal(["Rikka"], Names(GiftRecipients.Suggest([Friend("Zeus"), Friend("Rikka")], "", "zeus")));

    [Fact]
    public void TextMatchingNoContactSuggestsNothing() =>
        Assert.Empty(GiftRecipients.Suggest([Friend("Rikka")], "xyz", "Zeus"));

    [Fact]
    public void ExactFindsTheContactWhateverTheCase() =>
        Assert.Equal("Rikka", GiftRecipients.Exact([Friend("Rikka")], " RIKKA ")?.Name);
}
