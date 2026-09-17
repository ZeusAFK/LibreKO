using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<int, float, float, float>? CorpseEvent;

    private void HandleCorpse(Packet p)
    {
        if (p.RemainingBytes < 8) return;
        int charId = p.ReadShort();
        float x = p.ReadShort() / 10f;
        float z = p.ReadShort() / 10f;
        float y = p.ReadShort() / 10f;
        CorpseEvent?.Invoke(charId, x, z, y);
    }

    public void SendCorpseQuery(int charId)
    {
        var p = new Packet(GameOpcodes.GS_CORPSE);
        p.WriteShort((short)charId);
        _conn.Send(p);
    }
}
