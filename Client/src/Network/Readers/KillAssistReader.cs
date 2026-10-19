namespace LibreKO.Network.Readers;

internal sealed class KillAssistReader : IPacketReader
{
    private readonly KillAssistSession killAssist;

    public KillAssistReader(KillAssistSession killAssist)
    {
        this.killAssist = killAssist;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_KILL_ASSIST, HandleKillAssist);
    }

    private void HandleKillAssist(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        switch ((KillAssistSubOpcode)p.ReadByte())
        {
            case KillAssistSubOpcode.Kill:
                if (KillAssistReplies.ReadKill(p) is { } kill) killAssist.RaiseKillStreak(kill);
                break;
            case KillAssistSubOpcode.Assist:
                if (KillAssistReplies.ReadAssist(p) is { } assist) killAssist.RaiseAssist(assist);
                break;
        }
    }
}
