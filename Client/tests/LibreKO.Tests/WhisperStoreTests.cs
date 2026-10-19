using System;
using LibreKO.Domain;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class WhisperStoreTests
{
    private const string Partner = "Alpha";
    private const long PostId = 42;
    private static readonly DateTime At = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WhispersAreKeptPerPartnerWhateverTheCase()
    {
        var media = new RoomMediaSession();
        media.RecordWhisper(Partner, RoomPost.Line(1, Partner, "hi", At));
        Assert.Single(media.WhisperHistory(Partner.ToLowerInvariant()));
        Assert.Empty(media.WhisperHistory("Bravo"));
    }

    [Fact]
    public void AnotherCharacterStartsWithNoWhispersAndNoOpenWindows()
    {
        var media = new RoomMediaSession();
        media.ForgetWhispersUnlessOf("Mine");
        media.RecordWhisper(Partner, RoomPost.Line(1, Partner, "hi", At));
        media.KeepOpenWhispers([new OpenWhisper(Partner, 10f, 20f, Minimized: false, Unread: true)]);

        media.ForgetWhispersUnlessOf("mine");
        Assert.Single(media.OpenWhispers);
        Assert.Single(media.WhisperHistory(Partner));

        media.ForgetWhispersUnlessOf("Other");
        Assert.Empty(media.OpenWhispers);
        Assert.Empty(media.WhisperHistory(Partner));
    }

    [Fact]
    public void APostIsFoundInTheWhisperItWasPostedTo()
    {
        var media = new RoomMediaSession();
        var post = new RoomPost(PostId, RoomPostKind.Text, 1, Partner, At, "hi", 0, 0);
        media.RecordWhisper(Partner, post);

        Assert.Same(post, media.FindChatPost(PostId));
        Assert.Equal(Partner, media.DirectPartnerOf(PostId));
        Assert.Null(media.FindChatPost(PostId + 1));
        Assert.Equal("", media.DirectPartnerOf(PostId + 1));
    }
}
