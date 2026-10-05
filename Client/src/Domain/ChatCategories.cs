using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

[Flags]
public enum ChatCategory
{
    None = 0, General = 1, Whisper = 2, Party = 4, Clan = 8, Alliance = 16, Shout = 32, Trade = 64,
    Commander = 128, Notice = 256, System = 512, All = 1023,
}

public static class ChatCategories
{
    public static readonly ChatCategory[] Each =
    {
        ChatCategory.General, ChatCategory.Whisper, ChatCategory.Party, ChatCategory.Clan, ChatCategory.Alliance,
        ChatCategory.Shout, ChatCategory.Trade, ChatCategory.Commander, ChatCategory.Notice, ChatCategory.System,
    };

    public static ChatCategory Of(byte type, bool serverNotice)
    {
        if (serverNotice) return ChatCategory.Notice;
        return type switch
        {
            ChatType.General or ChatType.GameMaster or ChatType.SeekingParty or ChatType.ChatRoom
                or ChatType.ClanRecruit => ChatCategory.General,
            ChatType.Private => ChatCategory.Whisper,
            ChatType.Party or ChatType.Forced => ChatCategory.Party,
            ChatType.Clan or ChatType.ClanOfficer or ChatType.ClanNotice => ChatCategory.Clan,
            ChatType.Alliance => ChatCategory.Alliance,
            ChatType.Shout => ChatCategory.Shout,
            ChatType.Merchant => ChatCategory.Trade,
            ChatType.Command => ChatCategory.Commander,
            _ => ChatCategory.Notice,
        };
    }

    public static string Caption(ChatCategory category) => category switch
    {
        ChatCategory.General => "General",
        ChatCategory.Whisper => "Whispers",
        ChatCategory.Party => "Party",
        ChatCategory.Clan => "Clan",
        ChatCategory.Alliance => "Alliance",
        ChatCategory.Shout => "Shout",
        ChatCategory.Trade => "Trade",
        ChatCategory.Commander => "Commander",
        ChatCategory.Notice => "Notices",
        ChatCategory.System => "System",
        _ => "",
    };
}

public sealed record ChatTabSpec(string Id, string Caption, ChatCategory Filter, byte SendChannel, byte Tint);

public readonly record struct ChatTabFit(int Visible, int SelectedSlot);

public static class ChatTabs
{
    public const byte NoSendChannel = 0;

    public static IReadOnlyList<ChatTabSpec> All { get; } = new ChatTabSpec[]
    {
        new("all", "All", ChatCategory.All, NoSendChannel, ChatType.General),
        new("general", "General", ChatCategory.General | ChatCategory.Shout | ChatCategory.Commander | ChatCategory.Notice, ChatType.General, ChatType.General),
        new("whisper", "Whispers", ChatCategory.Whisper, ChatType.Private, ChatType.Private),
        new("party", "Party", ChatCategory.Party | ChatCategory.Notice, ChatType.Party, ChatType.Party),
        new("clan", "Clan", ChatCategory.Clan | ChatCategory.Alliance | ChatCategory.Notice, ChatType.Clan, ChatType.Clan),
        new("alliance", "Alliance", ChatCategory.Alliance | ChatCategory.Notice, ChatType.Alliance, ChatType.Alliance),
        new("shout", "Shout", ChatCategory.Shout, ChatType.Shout, ChatType.Shout),
        new("trade", "Trade", ChatCategory.Trade, NoSendChannel, ChatType.Merchant),
        new("system", "System", ChatCategory.Notice | ChatCategory.System, NoSendChannel, ChatType.WarSystem),
    };

    public static int IndexOf(string id)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].Id == id) return i;
        return -1;
    }

    public static ChatTabFit Fit(IReadOnlyList<float> widths, float available, float gap, float overflowWidth, int selected)
    {
        float total = 0;
        for (int i = 0; i < widths.Count; i++) total += widths[i] + (i > 0 ? gap : 0);
        if (total <= available) return new ChatTabFit(widths.Count, selected);

        float room = available - overflowWidth - gap;
        int visible = 0;
        float used = 0;
        while (visible < widths.Count)
        {
            float next = used + widths[visible] + (visible > 0 ? gap : 0);
            if (next > room) break;
            used = next;
            visible++;
        }
        visible = Math.Max(1, visible);
        int slot = selected < visible ? selected : visible - 1;
        return new ChatTabFit(visible, slot);
    }
}

public static class ChatUnread
{
    public static bool Marks(ChatCategory category) =>
        category is ChatCategory.Whisper or ChatCategory.Party or ChatCategory.Clan or ChatCategory.Alliance;

    public static List<int> TabsToMark(IReadOnlyList<ChatCategory> filters, ChatCategory category, int selected)
    {
        var tabs = new List<int>();
        if (!Marks(category)) return tabs;
        for (int i = 1; i < filters.Count; i++)
            if (i != selected && (filters[i] & category) != 0) tabs.Add(i);
        return tabs;
    }
}
