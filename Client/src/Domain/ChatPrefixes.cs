namespace LibreKO.Domain;

public static class ChatPrefixes
{
    public const byte NotChat = 0;

    public static byte ChannelFor(string text, byte selected)
    {
        if (string.IsNullOrEmpty(text)) return selected;
        return text[0] switch
        {
            '@' => ChatType.Private,
            '!' => ChatType.Shout,
            '#' => ChatType.Party,
            '$' => ChatType.Clan,
            '%' => ChatType.Command,
            '&' => ChatType.Alliance,
            '~' => ChatType.ClanOfficer,
            '|' => ChatType.ClanRecruit,
            '\\' => ChatType.ChatRoom,
            '/' or '+' => NotChat,
            _ => selected,
        };
    }
}
