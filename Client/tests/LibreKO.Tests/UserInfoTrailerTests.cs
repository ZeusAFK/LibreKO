using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class UserInfoTrailerTests
{
    private const int CharacterId = 4242;
    private const ushort TitleId = 17;

    private static Packet Record(bool helmetHidden, bool costumeHidden, bool genie, int transformSkill = 0)
    {
        var p = new Packet(GameOpcodes.GS_USER_INOUT);
        p.WriteSByteString("Aurelia");
        p.WriteByte(1);
        p.WriteByte(0); p.WriteByte(0); p.WriteByte(0);
        p.WriteShort(0);
        p.WriteByte(0);
        p.WriteInt(0); p.WriteShort(0); p.WriteByte(0); p.WriteUShort(93); p.WriteInt(0); p.WriteByte(0);
        p.WriteByte(62);
        p.WriteByte(1);
        p.WriteShort(105);
        p.WriteShort(5430); p.WriteShort(3770); p.WriteShort(120);
        p.WriteByte(2);
        p.WriteInt(7);
        p.WriteByte(1);
        p.WriteByte(1); p.WriteInt(transformSkill); p.WriteByte(0);
        p.WriteByte(0);
        p.WriteByte(0);
        p.WriteByte(0);
        p.WriteByte(0); p.WriteByte(0); p.WriteByte(0);
        p.WriteShort(90);
        p.WriteByte(0); p.WriteByte(0); p.WriteShort(0);
        p.WriteByte(0); p.WriteByte(0);
        for (int i = 0; i < InventoryConstants.VisualSlotCount; i++)
        {
            p.WriteInt(0); p.WriteShort(0); p.WriteByte(0);
        }
        p.WriteUShort(21); p.WriteInt(-1); p.WriteByte(0); p.WriteInt(0);
        p.WriteByte(helmetHidden ? (byte)1 : (byte)0);
        p.WriteByte(costumeHidden ? (byte)1 : (byte)0);
        p.WriteByte(genie ? (byte)1 : (byte)0);
        p.WriteByte(0); p.WriteShort(0); p.WriteInt(0); p.WriteByte(0); p.WriteByte(0); p.WriteByte(1);
        p.WriteUShort(TitleId);
        p.ResetOffset();
        return p;
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void TheHelmetCostumeAndGenieFlagsAreReadInOrder(bool helmetHidden, bool costumeHidden, bool genie)
    {
        var p = Record(helmetHidden, costumeHidden, genie);

        var e = WorldWire.ReadUserInfo(p, CharacterId);

        Assert.Equal(helmetHidden, e.HelmetHidden);
        Assert.Equal(costumeHidden, e.CostumeHidden);
        Assert.Equal(genie, e.GenieActive);
        Assert.Equal(TitleId, e.TitleId);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void ATransformedPlayerCarriesItsTransformationSkill()
    {
        const int transformSkill = 450003;

        var e = WorldWire.ReadUserInfo(Record(false, false, false, transformSkill), CharacterId);

        Assert.Equal(transformSkill, e.TransformSkill);
        Assert.Equal(TitleId, e.TitleId);
    }
}
