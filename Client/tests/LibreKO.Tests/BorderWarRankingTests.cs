using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class BorderWarRankingTests
{
    private static void Row(Packet p, string name, byte nation, ushort clanId, string clan, int points)
    {
        p.WriteString(name);
        p.WriteByte(nation);
        p.WriteUShort(clanId);
        p.WriteUShort(0);
        p.WriteString(clan);
        p.WriteInt(points);
    }

    [Fact]
    public void TheBoardReadsBothNationsAndTheExpTail()
    {
        var p = new Packet(GameOpcodes.GS_RANK);
        p.WriteByte((byte)RankType.BorderDefenseWar);
        p.WriteUShort(2);
        Row(p, "Rikka", 1, 12, "Olympus", 21);
        Row(p, "Rin", 1, 0, "", 4);
        p.WriteUShort(1);
        Row(p, "Zeus", 2, 0, "", 7);
        p.WriteUInt(150000);
        p.WriteUInt(0);
        p.ResetOffset();
        p.ReadByte();

        var board = BorderWarRanking.Read(p);

        Assert.Equal(2, board.Karus.Count);
        Assert.Equal("Rikka", board.Karus[0].Name);
        Assert.Equal("Olympus", board.Karus[0].ClanName);
        Assert.Equal(21, board.Karus[0].Points);
        Assert.Equal("Rin", board.Karus[1].Name);
        Assert.Single(board.Elmorad);
        Assert.Equal(7, board.Elmorad[0].Points);
        Assert.Equal(150000u, board.VictoryExp);
        Assert.Equal(0u, board.BonusExp);
    }

    [Fact]
    public void AnEmptyBoardHasNoRows()
    {
        var p = new Packet(GameOpcodes.GS_RANK);
        p.WriteByte((byte)RankType.BorderDefenseWar);
        p.WriteUShort(0);
        p.WriteUShort(0);
        p.WriteUInt(0);
        p.WriteUInt(0);
        p.ResetOffset();
        p.ReadByte();

        var board = BorderWarRanking.Read(p);

        Assert.Empty(board.Karus);
        Assert.Empty(board.Elmorad);
    }
}
