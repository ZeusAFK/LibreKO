namespace LibreKO.Network.Readers;

internal sealed class PvpReader : IPacketReader
{
    private readonly PvpSession pvp;

    public PvpReader(PvpSession pvp)
    {
        this.pvp = pvp;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_PVP, HandlePvp);
    }

    private void HandlePvp(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (PvpSubOpcode)p.ReadByte();
        switch (sub)
        {
            case PvpSubOpcode.AssignRival:
                {
                    if (p.RemainingBytes < 4) return;
                    int rivalCharId = p.ReadInt();
                    if (p.RemainingBytes >= 4) p.ReadInt();
                    if (p.RemainingBytes >= 4) p.ReadInt();
                    if (p.RemainingBytes >= 4) { p.ReadUShort(); p.ReadUShort(); }
                    string clanName = p.RemainingBytes >= 2 ? p.ReadString() : "";
                    string rivalName = p.RemainingBytes >= 2 ? p.ReadString() : "";
                    pvp.RaisePvpRivalAssigned(rivalCharId, rivalName, clanName);
                    break;
                }

            case PvpSubOpcode.RemoveRival:
                pvp.RaisePvpRivalRemoved();
                break;

            case PvpSubOpcode.UpdateHelmet:
                {
                    int gauge = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
                    bool full = p.RemainingBytes >= 1 && p.ReadByte() != 0;
                    pvp.ApplyAnger(gauge, full);
                    break;
                }

            case PvpSubOpcode.ResetHelmet:
                pvp.ApplyAnger(0, false);
                break;
        }
    }
}
