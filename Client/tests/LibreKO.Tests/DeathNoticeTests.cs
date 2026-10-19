using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class DeathNoticeTests
{
    private static Packet Notice(byte first, byte second, byte king, byte kind)
    {
        var p = new Packet(GameOpcodes.GS_CHAT);
        p.WriteByte(first);
        p.WriteByte(second);
        p.WriteByte(king);
        p.WriteByte(kind);
        p.WriteInt(7);
        p.WriteSByteString("Rin");
        p.WriteInt(9);
        p.WriteSByteString("Kai");
        p.WriteShort(100);
        p.WriteShort(200);
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void TheKillersNationComesFirst()
    {
        var notice = ChatWire.ReadDeathNotice(Notice(Nations.Karus, Nations.ElMorad, 1, DeathNotices.CoordinatesKind))!.Value;

        Assert.Equal((Nations.Karus, Nations.ElMorad, true), (notice.KillerNation, notice.VictimNation, notice.VictimIsKing));
        Assert.Equal(("Rin", "Kai", 100, 200), (notice.KillerName, notice.VictimName, notice.X, notice.Z));
    }

    [Fact]
    public void ADefeatedNoticeSwapsTheNations()
    {
        var notice = ChatWire.ReadDeathNotice(Notice(Nations.Karus, Nations.ElMorad, 0, DeathNotices.DefeatedKind))!.Value;

        Assert.Equal((Nations.ElMorad, Nations.Karus), (notice.KillerNation, notice.VictimNation));
    }

    [Theory]
    [InlineData(DeathNotices.CoordinatesKind, 61, true, Nations.Karus, DeathNotices.ElMoradKingDownText)]
    [InlineData(DeathNotices.CoordinatesKind, 66, true, Nations.ElMorad, DeathNotices.KarusKingDownText)]
    [InlineData(DeathNotices.CoordinatesKind, 71, true, Nations.Karus, DeathNotices.CoordinatesText)]
    [InlineData(DeathNotices.CoordinatesKind, 61, false, Nations.Karus, DeathNotices.CoordinatesText)]
    [InlineData(DeathNotices.DefeatedKind, 61, true, Nations.Karus, DeathNotices.DefeatedText)]
    [InlineData(DeathNotices.AvengedKind, 21, false, Nations.Karus, DeathNotices.AvengedText)]
    public void TheKingLineOnlyAppearsInTheWarZones(int kind, int zone, bool king, int killerNation, int text)
    {
        Assert.Equal(text, DeathNotices.TextFor(kind, zone, king, killerNation));
    }

    [Fact]
    public void NamesTakeTheirNationsColourAndOwnKillsInZone85AreOrchid()
    {
        Assert.Equal(DeathNotices.KarusNameColour, DeathNotices.NameColour(Nations.Karus));
        Assert.Equal(DeathNotices.ElMoradNameColour, DeathNotices.NameColour(Nations.ElMorad));
        Assert.Equal(DeathNotices.OwnKillColour, DeathNotices.LineColourFor(DeathNotices.OwnKillZone, killedByMe: true));
        Assert.Equal(DeathNotices.LineColour, DeathNotices.LineColourFor(71, killedByMe: true));
    }
}
