using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class RoulettePacketWriter
{

    public const byte ResultFail = 0;
    public const byte ResultOk = 1;

    public const int PrizeListMaxEntries = 20;
    public const int PrizeListPageSize = 10;

    public readonly record struct PrizeLogEntry(int ItemId, int Quantity, int UnixTime);

    private readonly List<PrizeLogEntry> _prizeLog = [];

    public EventBoardSubOpcode Sub { get; set; }
    public int Page { get; set; }
    public int Status { get; set; }
    public int Coins { get; set; }
    public byte SpinResult { get; set; }
    public int PrizeItemId { get; set; }
    public int PrizeGold { get; set; }

    public static Packet Open(int coins) =>
        new RoulettePacketWriter { Sub = EventBoardSubOpcode.RouletteOpen, Coins = coins }.Build();

    public static Packet Spin(byte result, int prizeItemId, int prizeGold) =>
        new RoulettePacketWriter
        {
            Sub = EventBoardSubOpcode.RouletteSpin,
            SpinResult = result,
            PrizeItemId = prizeItemId,
            PrizeGold = prizeGold,
        }.Build();

    public static Packet PrizeList(int page, IEnumerable<PrizeLogEntry> entries)
    {
        var writer = new RoulettePacketWriter { Sub = EventBoardSubOpcode.RoulettePrizeList, Page = page, Status = ResultOk };
        foreach (var entry in entries)
        {
            if (writer._prizeLog.Count == PrizeListMaxEntries)
                break;
            writer._prizeLog.Add(entry);
        }

        return writer.Build();
    }

    private Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_EVENT_BOARD);
        packet.WriteByte((byte)Sub);

        switch (Sub)
        {
            case EventBoardSubOpcode.RouletteOpen:
                packet.WriteInt(Coins);
                break;

            case EventBoardSubOpcode.RouletteSpin:
                packet.WriteByte(SpinResult);
                packet.WriteInt(PrizeItemId);
                packet.WriteInt(PrizeGold);
                break;

            case EventBoardSubOpcode.RoulettePrizeList:
                packet.WriteInt(Page);
                packet.WriteInt(Status);
                packet.WriteInt(_prizeLog.Count);
                foreach (var entry in _prizeLog)
                {
                    packet.WriteInt(entry.ItemId);
                    packet.WriteInt(entry.Quantity);
                    packet.WriteInt(entry.UnixTime);
                }

                break;
        }

        return packet;
    }
}
