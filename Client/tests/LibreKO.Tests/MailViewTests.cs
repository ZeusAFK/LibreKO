using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class MailViewTests
{
    private static MailEntry Mail(bool read, MailAttachmentState attachments) =>
        new() { Read = read, Attachments = attachments };

    [Fact]
    public void ParcelsAreLettersWithAttachmentsStillWaiting()
    {
        var mails = new[]
        {
            Mail(true, MailAttachmentState.Pending),
            Mail(false, MailAttachmentState.Claimed),
            Mail(false, MailAttachmentState.None),
            Mail(false, MailAttachmentState.Pending),
        };

        Assert.Equal(2, MailView.Parcels(mails));
    }

    [Fact]
    public void TheTwoFiltersCombine()
    {
        var readParcel = Mail(true, MailAttachmentState.Pending);
        var unreadLetter = Mail(false, MailAttachmentState.None);

        Assert.True(MailView.Shows(readParcel, unreadOnly: false, parcelsOnly: true));
        Assert.False(MailView.Shows(readParcel, unreadOnly: true, parcelsOnly: true));
        Assert.False(MailView.Shows(unreadLetter, unreadOnly: false, parcelsOnly: true));
        Assert.True(MailView.Shows(unreadLetter, unreadOnly: true, parcelsOnly: false));
    }

    [Fact]
    public void TheFillCountsAgainstTheInboxCap()
    {
        Assert.Equal("12 / 50 letters", MailView.Fill(12));
        Assert.False(MailView.Full(49));
        Assert.True(MailView.Full(MailWire.InboxMax));
    }
}
