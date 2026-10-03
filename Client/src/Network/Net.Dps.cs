using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    // Raised when the server broadcasts the party's accumulated damage table.
    // windowStart is a unix-seconds timestamp shared by all members.
    public event System.Action<long, List<(int CharId, long Damage)>>? PartyDpsEvent;

    private void HandlePartyDps(Packet p)
    {
        long windowStart = p.ReadLong();
        int count = p.ReadShort();
        if (count < 0 || count > 16)
            return;

        var entries = new List<(int, long)>(count);
        for (int i = 0; i < count; i++)
        {
            if (p.RemainingBytes < 12)
                break;
            int charId = p.ReadInt();
            long damage = p.ReadLong();
            entries.Add((charId, damage));
        }

        PartyDpsEvent?.Invoke(windowStart, entries);
    }
}
