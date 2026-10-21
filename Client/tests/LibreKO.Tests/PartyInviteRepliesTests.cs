using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class PartyInviteRepliesTests
{
    private const int InviteeId = 70002;
    private const int InviterId = 70003;

    [Fact]
    public void ASentInviteNamesTheInvitee()
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteInt(InviteeId);
        p.WriteString("Aurelia");
        p.ResetOffset();

        var sent = PartyInviteSent.Read(p);

        Assert.NotNull(sent);
        Assert.Equal(InviteeId, sent.Value.InviteeId);
        Assert.Equal("Aurelia", sent.Value.InviteeName);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void AClosedInviteNamesTheInviteeAndAWithdrawnOneTheInviter()
    {
        var closed = new Packet(GameOpcodes.GS_PARTY);
        closed.WriteInt(InviteeId);
        closed.ResetOffset();
        var withdrawn = new Packet(GameOpcodes.GS_PARTY);
        withdrawn.WriteInt(InviterId);
        withdrawn.ResetOffset();

        Assert.Equal(InviteeId, PartyInviteClosed.Read(closed)?.InviteeId);
        Assert.Equal(InviterId, PartyInviteWithdrawn.Read(withdrawn)?.InviterId);
    }

    [Fact]
    public void AShortPacketIsNoInvite()
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteShort(1);
        p.ResetOffset();

        Assert.Null(PartyInviteClosed.Read(p));
    }
}
