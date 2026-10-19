using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class NoahKnightsTests
{
    [Theory]
    [InlineData(1, ClanRanks.None, true)]
    [InlineData(NoahKnights.MaxLevel, ClanRanks.None, true)]
    [InlineData(NoahKnights.GraduationLevel, ClanRanks.None, false)]
    [InlineData(30, ClanRanks.Chief, false)]
    public void AClanlessPlayerUpToLevelFiftyIsANoahKnight(int level, byte duty, bool member) =>
        Assert.Equal(member, NoahKnights.IsMember(level, duty));

    [Theory]
    [InlineData("", 30, ClanRanks.None, false, true)]
    [InlineData("Wolves", 30, ClanRanks.None, false, false)]
    [InlineData("", 30, ClanRanks.None, true, false)]
    [InlineData("", 60, ClanRanks.None, false, false)]
    public void ThePlateLineGivesWayToAClanNameAndToAStall(string clan, int level, byte duty, bool stall, bool shown) =>
        Assert.Equal(shown, NoahKnights.ShowsPlateLine(clan, level, duty, stall));

    [Fact]
    public void MyInfoCarriesTheMannerPointsAfterTheChickenByte()
    {
        var p = new Packet(GameOpcodes.GS_MYINFO);
        for (int i = 0; i < InventoryConstants.MyInfoReservedItemRecords * 19; i++) p.WriteByte(0);
        p.WriteByte(0);
        p.WriteByte(0);
        p.WriteByte(0);
        p.WriteByte(1);
        p.WriteInt(1_234);
        for (int i = 0; i < 6; i++) p.WriteByte(0);
        p.WriteShort(0);
        p.WriteByte(2);
        for (int i = 0; i < 5; i++) p.WriteByte(3);
        p.ResetOffset();

        MyInfoWire.ReadTail(p, out int manner, out int rebirth, out _, out _, out _, out _, out _, out _);

        Assert.Equal(1_234, manner);
        Assert.Equal(2, rebirth);
    }
}
