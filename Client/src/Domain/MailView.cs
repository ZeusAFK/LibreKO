using System.Collections.Generic;
using System.Linq;
using LibreKO.Network;

namespace LibreKO.Domain;

public static class MailView
{
    public static bool IsParcel(MailEntry mail) => mail.Attachments == MailAttachmentState.Pending;

    public static bool Shows(MailEntry mail, bool unreadOnly, bool parcelsOnly) =>
        (!unreadOnly || !mail.Read) && (!parcelsOnly || IsParcel(mail));

    public static int Parcels(IEnumerable<MailEntry> mails) => mails.Count(IsParcel);

    public static string EmptyText(bool unreadOnly, bool parcelsOnly) => (unreadOnly, parcelsOnly) switch
    {
        (true, true) => "No unread parcels.",
        (false, true) => "No parcels waiting.",
        _ => "No unread mail.",
    };

    public static string Fill(int count) => $"{count} / {MailWire.InboxMax} letters";

    public static bool Full(int count) => count >= MailWire.InboxMax;
}
