using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<int>? SantaEvent;

    private void HandleSanta(Packet p)
    {
        int state = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        SantaEvent?.Invoke(state);
    }
}
