namespace LibreKO.Network.Readers;

internal sealed class VanguardReader : IPacketReader
{
    private readonly VanguardSession vanguard;

    public VanguardReader(VanguardSession vanguard)
    {
        this.vanguard = vanguard;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_VANGUARD, HandleVanguard);
    }

    private void HandleVanguard(Packet p)
    {
        if (VanguardReply.Read(p) is not { } reply) return;
        switch (reply.Kind)
        {
            case VanguardReplyKind.Appointed:
                vanguard.ApplyAppointed(true);
                break;
            case VanguardReplyKind.AppointFailed:
                vanguard.ApplyAppointed(false);
                break;
            case VanguardReplyKind.List:
                vanguard.ApplyWantedList(reply.Names);
                break;
            case VanguardReplyKind.Positions:
                vanguard.ApplyPositions(reply.Names, reply.Marks);
                break;
            case VanguardReplyKind.Remove:
                vanguard.ForgetWanted(reply.Names[0]);
                break;
            case VanguardReplyKind.Closed:
                vanguard.Reset();
                break;
        }
        vanguard.RaiseVanguard(reply);
    }
}
