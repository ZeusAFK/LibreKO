namespace LibreKO.Domain;

public static class ChatPrefixes
{
    public const byte NotChat = 0;

    public static byte ChannelFor(string text, byte selected)
    {
        if (string.IsNullOrEmpty(text)) return selected;
        return text[0] switch
        {
            '@' => (byte)ChatType.Private,
            '!' => (byte)ChatType.Shout,
            '#' => (byte)ChatType.Party,
            '$' => (byte)ChatType.Clan,
            '%' => (byte)ChatType.Command,
            '&' => (byte)ChatType.Alliance,
            '~' => (byte)ChatType.ClanOfficer,
            '|' => (byte)ChatType.NoahKnights,
            '/' or '+' => NotChat,
            _ => selected,
        };
    }
}
