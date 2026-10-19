namespace LibreKO.Network;

public enum MailSubOpcode : byte
{
    List = 1,
    Read = 2,
    Send = 3,
    Delete = 4,
    Claim = 5,
    Unread = 6,
    ClaimAttachment = 7,
}

public static class MailWire
{
    public const int SubjectMax = 64;
    public const int BodyMax = 512;
    public const int InboxMax = 50;
    public const int ItemAttachmentsMax = 4;
}

public enum MailAttachmentKind : byte
{
    Item = 0,
    Gold = 1,
    Experience = 2,
    NationalPoints = 3,
}

public enum MailAttachmentState : byte
{
    None = 0,
    Pending = 1,
    Claimed = 2,
}

public class MailAttachment
{
    public MailAttachmentKind Kind;
    public int ItemId;
    public int Count;
    public int Claimed;

    public int Remaining => System.Math.Max(0, Count - Claimed);
}

public readonly record struct MailItemPick(byte Slot, ushort Count);

public enum MailKind : byte
{
    Player = 0,
    System = 1,
    Store = 2,
}

public class MailEntry
{
    public int Id;
    public string Sender = string.Empty;
    public string Subject = string.Empty;
    public bool Read;
    public MailAttachmentState Attachments;
    public MailKind Kind;
    public DateTime SentAt;
    public List<MailAttachment> Items = [];
}
