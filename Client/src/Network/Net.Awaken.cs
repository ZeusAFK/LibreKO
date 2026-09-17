using System;

namespace LibreKO.Network;

public partial class Net
{
    private const byte AwakenEffectTypeVisual = 1;

    public event Action<float, int>? AwakenEvent;

    private void HandleAwaken(Packet p)
    {
        if (p.RemainingBytes < 4 + 1 + 4) return;
        float scale = p.ReadFloat();
        byte type = p.ReadByte();
        int effectId = p.ReadInt();
        if (type != AwakenEffectTypeVisual) return;
        AwakenEvent?.Invoke(scale, effectId);
    }
}
