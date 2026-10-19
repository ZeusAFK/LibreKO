using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class EffectReplyTests
{
    private const int Player = 4021;
    private const int Strike = 106800;
    private const int Item = 389310000;

    [Fact]
    public void AnEntityEffectCarriesItsValue()
    {
        var p = new Packet(GameOpcodes.GS_EFFECT);
        p.WriteInt(Player);
        p.WriteInt(Strike);
        p.ResetOffset();

        Assert.Equal(new EffectReply(Player, Strike, ""), EffectReply.Read(p));
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void AnAnnouncementAddsTheWinnerName()
    {
        var p = new Packet(GameOpcodes.GS_EFFECT);
        p.WriteInt(EffectWire.AnnouncementEntity);
        p.WriteInt(Item);
        p.WriteSByteString("Rikka");
        p.ResetOffset();

        Assert.Equal(new EffectReply(EffectWire.AnnouncementEntity, Item, "Rikka"), EffectReply.Read(p));
        Assert.Equal(0, p.RemainingBytes);
    }
}
