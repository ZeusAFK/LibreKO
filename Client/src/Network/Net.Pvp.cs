using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<int, string, string>? PvpRivalAssignedEvent;
    public event Action? PvpRivalRemovedEvent;
    public event Action<int, bool>? PvpAngerEvent;

    private const byte PvpAssignRival = 1;
    private const byte PvpRemoveRival = 2;
    private const byte PvpUpdateHelmet = 5;
    private const byte PvpResetHelmet = 6;

    private void HandlePvp(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case PvpAssignRival:
            {
                if (p.RemainingBytes < 4) return;
                int rivalCharId = p.ReadInt();
                if (p.RemainingBytes >= 4) p.ReadInt();
                if (p.RemainingBytes >= 4) p.ReadInt();
                if (p.RemainingBytes >= 4) { p.ReadUShort(); p.ReadUShort(); }
                string clanName = p.RemainingBytes >= 2 ? p.ReadString() : "";
                string rivalName = p.RemainingBytes >= 2 ? p.ReadString() : "";
                PvpRivalAssignedEvent?.Invoke(rivalCharId, rivalName, clanName);
                break;
            }

            case PvpRemoveRival:
                PvpRivalRemovedEvent?.Invoke();
                break;

            case PvpUpdateHelmet:
            {
                int gauge = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
                bool full = p.RemainingBytes >= 1 && p.ReadByte() != 0;
                PvpAngerEvent?.Invoke(gauge, full);
                break;
            }

            case PvpResetHelmet:
                PvpAngerEvent?.Invoke(0, false);
                break;
        }
    }
}
