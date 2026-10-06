using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<int>? TransformationListEvent;
    public event Action<int>? TransformationRefusedEvent;

    private bool HandleTransformationMagic(int sub, Packet p)
    {
        if (sub == MagicSub.TransformationList)
        {
            if (p.RemainingBytes >= 4) TransformationListEvent?.Invoke(p.ReadInt());
            return true;
        }
        if (sub == MagicSub.TransformationRefused)
        {
            if (p.RemainingBytes >= 1) TransformationRefusedEvent?.Invoke(p.ReadByte());
            return true;
        }
        return false;
    }
}
