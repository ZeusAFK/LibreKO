using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<int, int, int, long, long, int, int, int, int>? LevelChangeEvent;
    public event Action<int, int>? PeerLevelChangeEvent;
    public event Action<int, int>? LoyaltyChangeEvent;
    public event Action<int>? WeightChangeEvent;
    public event Action<int, int, int>? StateChangeEvent;

    private void HandleLevelChange(Packet p)
    {
        if (p.RemainingBytes < 5) return;
        int charId = p.ReadInt();
        if (charId != MyCharId)
        {
            PeerLevelChangeEvent?.Invoke(charId, p.ReadByte());
            return;
        }
        int level = p.ReadByte();
        int statPoints = p.ReadShort();
        int skillPool = p.ReadByte();
        long maxExp = p.ReadLong();
        long exp = p.ReadLong();
        int maxHp = p.ReadShort(); int hp = p.ReadShort();
        int maxMp = p.ReadShort(); int mp = p.ReadShort();
        LevelChangeEvent?.Invoke(level, statPoints, skillPool, maxExp, exp, maxHp, hp, maxMp, mp);
    }

    private void HandleLoyaltyChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        int type = p.ReadByte();
        if (type != 1) return;
        int loyalty = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        int monthly = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        LoyaltyChangeEvent?.Invoke(loyalty, monthly);
    }

    private void HandleWeightChange(Packet p)
    {
        if (p.RemainingBytes < 4) return;
        WeightChangeEvent?.Invoke(p.ReadInt());
    }

    private void HandleStateChange(Packet p)
    {
        if (p.RemainingBytes < 5) return;
        int charId = p.ReadInt();
        int type = p.ReadByte();
        int value = p.RemainingBytes >= 4 ? p.ReadInt()
                  : p.RemainingBytes >= 1 ? p.ReadByte()
                  : 0;
        StateChangeEvent?.Invoke(charId, type, value);
    }
}
