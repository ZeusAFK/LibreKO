using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action? ClanBattleNotifyEvent;

    public event Action<int>? ClanBattlePointsEvent;

    private void HandleClanBattle(Packet p)
    {
        ClanBattleNotifyEvent?.Invoke();
    }

    private void HandleClanBattlePoints(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        p.ReadByte();
        int sub = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        ClanBattlePointsEvent?.Invoke(sub);
    }
}
