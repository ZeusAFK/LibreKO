using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class WarpListPacketWriter
{

    public const byte ResultArrived = 1;
    public const byte ResultLevelTooLow = 2;
    public const byte ResultCastleSiege = 3;
    public const byte ResultNoNationalPoints = 5;
    public const byte ResultLevelRangeOnly = 6;
    public const byte ResultNotQualified = 7;
    public const byte ResultTradeCooldown = 8;
    public const byte ResultServerFull = 9;

    public readonly record struct Entry(
        short WarpId, string Name, string Announce, short ZoneId, short MaxUsers, uint Fee);

    private readonly WarpListSubOpcode _sub;
    private readonly byte _result;
    private readonly IReadOnlyCollection<Entry>? _entries;

    private WarpListPacketWriter(WarpListSubOpcode sub, byte result, IReadOnlyCollection<Entry>? entries)
    {
        _sub = sub;
        _result = result;
        _entries = entries;
    }

    public static Packet Menu(IReadOnlyCollection<Entry> entries) => new WarpListPacketWriter(WarpListSubOpcode.Menu, 0, entries).Build();
    public static Packet Arrived() => Result(ResultArrived);
    public static Packet Result(byte resultCode) => new WarpListPacketWriter(WarpListSubOpcode.Result, resultCode, null).Build();

    private Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_WARP_LIST);
        packet.WriteByte((byte)_sub);

        if (_entries is null)
        {
            packet.WriteByte(_result);
            return packet;
        }

        packet.WriteShort((short)_entries.Count);
        foreach (var entry in _entries)
        {
            packet.WriteShort(entry.WarpId);
            packet.WriteString(entry.Name);
            packet.WriteString(entry.Announce);
            packet.WriteShort(entry.ZoneId);
            packet.WriteShort(entry.MaxUsers);
            packet.WriteUInt(entry.Fee);
        }

        return packet;
    }
}
