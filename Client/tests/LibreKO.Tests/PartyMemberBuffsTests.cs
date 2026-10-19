using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class PartyMemberBuffsTests
{
    private const string TwoBuffs = "40711101000251A10700CDA40100";
    private const string NoBuffs = "407111010000";

    private static Packet FromServer(string hex)
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteBytes(Convert.FromHexString(hex));
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void TheServerListReadsAsTheMemberAndTheirSkillIds()
    {
        var p = FromServer(TwoBuffs);

        Assert.Equal((byte)PartySubOpcode.MemberBuffs, p.ReadByte());
        var buffs = PartyMemberBuffs.Read(p);

        Assert.NotNull(buffs);
        Assert.Equal(70001, buffs.Value.CharId);
        Assert.Equal(new[] { 500049, 107725 }, buffs.Value.SkillIds);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void AMemberWithNoBuffsReadsAsAnEmptyList()
    {
        var p = FromServer(NoBuffs);
        p.ReadByte();

        var buffs = PartyMemberBuffs.Read(p);

        Assert.NotNull(buffs);
        Assert.Equal(70001, buffs.Value.CharId);
        Assert.Empty(buffs.Value.SkillIds);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void AListShorterThanItsCountIsRejected()
    {
        var p = FromServer(TwoBuffs[..^2]);
        p.ReadByte();

        Assert.Null(PartyMemberBuffs.Read(p));
    }
}
