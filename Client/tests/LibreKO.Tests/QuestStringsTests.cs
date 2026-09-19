using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class QuestStringsTests
{
    private const string Fixture =
        "0F01003D000A0053696C6B2053706F6F6C1C004D656E697373696168207175696572652032206D616E7A616E61732E";

    private static Packet PacketFrom(string hex)
    {
        var packet = new Packet(GameOpcodes.GS_QUEST);
        packet.WriteBytes(Convert.FromHexString(hex));
        Assert.Equal((byte)QuestSub.Text, packet.ReadByte());
        return packet;
    }

    [Fact]
    public void ServerQuestStringPacketCarriesTheTitleAndJournal()
    {
        var texts = QuestStrings.ReadList(PacketFrom(Fixture));
        var text = Assert.Single(texts);
        Assert.Equal(61, text.QuestId);
        Assert.Equal("Silk Spool", text.Title);
        Assert.Equal("Menissiah quiere 2 manzanas.", text.Journal);
    }

    [Fact]
    public void AnEmptyListLeavesNothingBehind()
    {
        Assert.Empty(QuestStrings.ReadList(PacketFrom("0F0000")));
    }

    [Fact]
    public void TrailingBytesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => QuestStrings.ReadList(PacketFrom("0F000000")));
    }
}
