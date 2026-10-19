namespace LibreKO.Network;

public readonly record struct WarpListEntry(
        int WarpId, string Name, string Announce, int ZoneId, int MaxUsers, long Fee);
