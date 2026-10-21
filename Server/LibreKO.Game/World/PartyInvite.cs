namespace LibreKO.Game.World;

public readonly record struct PartyInvite(int PartyIndex, int InviterId)
{
    public static readonly PartyInvite None = new(-1, -1);

    public bool IsPending => PartyIndex >= 0;

    internal long Packed => (long)PartyIndex << 32 | (uint)InviterId;

    internal static PartyInvite Unpack(long packed) => new((int)(packed >> 32), (int)packed);
}
