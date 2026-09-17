using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    private readonly Dictionary<int, bool> _gmFxStates = new();
    public event Action<int, bool>? GmFxEvent;

    public bool GmFxVisible(int id, bool isGm) => isGm && _gmFxStates.GetValueOrDefault(id, true);

    private void HandleGmFx(Packet p)
    {
        if (p.RemainingBytes < 5) return;
        int id = p.ReadInt();
        bool enabled = p.ReadByte() == 1;
        _gmFxStates[id] = enabled;
        GmFxEvent?.Invoke(id, enabled);
    }
}
