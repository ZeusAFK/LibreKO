namespace LibreKO.Network;

public readonly record struct PartyInviteSent(int InviteeId, string InviteeName)
{
    public static PartyInviteSent? Read(Packet p)
    {
        if (p.RemainingBytes < sizeof(int)) return null;
        int id = p.ReadInt();
        return new PartyInviteSent(id, p.RemainingBytes >= sizeof(short) ? p.ReadString() : "");
    }
}

public readonly record struct PartyInviteClosed(int InviteeId)
{
    public static PartyInviteClosed? Read(Packet p) =>
        p.RemainingBytes >= sizeof(int) ? new PartyInviteClosed(p.ReadInt()) : null;
}

public readonly record struct PartyInviteWithdrawn(int InviterId)
{
    public static PartyInviteWithdrawn? Read(Packet p) =>
        p.RemainingBytes >= sizeof(int) ? new PartyInviteWithdrawn(p.ReadInt()) : null;
}
