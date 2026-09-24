using LibreKO.Domain;

namespace LibreKO.Network;

public partial class Net
{
    public event System.Action<int>? BuffExpiredEvent;

    public const byte TargetHpPollEcho = 1;

    public const byte SkillBarSave = 1;
    public const byte SkillBarLoad = 2;
    public const int SkillBarMaxSlots = HotbarLayout.Total;

    private void HandleTargetHp(Packet p)
    {
        int id = p.ReadInt();
        byte echo = p.ReadByte();
        int maxHp = p.ReadInt();
        int hp = p.ReadInt();
        int damage = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        if (echo == TargetHpPollEcho)
            EntityHpSyncEvent?.Invoke(id, hp, maxHp);
        else
            EntityHpEvent?.Invoke(id, hp, maxHp, damage);
    }

    private void HandleAttack(Packet p)
    {
        byte attackType = p.ReadByte();
        byte result = p.ReadByte();
        int attackerId = p.ReadInt();
        int targetId = p.ReadInt();
        AttackEvent?.Invoke(attackType, result, attackerId, targetId);
    }

    private void HandleMagicProcess(Packet p)
    {
        byte sub = p.ReadByte();
        if (sub == MagicSub.DurationExpired)
        {
            if (p.RemainingBytes >= 1) BuffExpiredEvent?.Invoke(p.ReadByte());
            return;
        }
        if (p.RemainingBytes < 12)
        {
            MagicEvent?.Invoke(sub, 0, 0, 0, new short[7]);
            return;
        }

        int skillId = p.ReadInt();
        int casterId = p.ReadInt();
        int targetId = p.ReadInt();
        var data = new short[7];
        for (int i = 0; i < 7 && p.RemainingBytes >= 4; i++)
            data[i] = (short)p.ReadInt();
        MagicEvent?.Invoke(sub, skillId, casterId, targetId, data);
    }

    private void HandleDead(Packet p)
    {
        int victim = p.ReadInt();
        int killer = p.RemainingBytes >= 4 ? p.ReadInt() : -1;
        DeadEvent?.Invoke(victim, killer);
    }

    private void HandleHpChange(Packet p)
    {
        int maxHp = p.ReadShort();
        int hp = p.ReadShort();
        int attackerId = p.RemainingBytes >= 4 ? p.ReadInt() : -1;
        SelfHpEvent?.Invoke(hp, maxHp, attackerId);
    }

    private void HandleMspChange(Packet p)
    {
        int maxMp = p.ReadShort();
        int mp = p.ReadShort();
        SelfMpEvent?.Invoke(mp, maxMp);
    }

    private void HandleRegene(Packet p)
    {
        if (p.RemainingBytes < 4) return;
        float x = p.ReadShort() / 10f;
        float z = p.ReadShort() / 10f;
        RegeneEvent?.Invoke(x, z);
    }

    private void HandleSkillData(Packet p)
    {
        if (p.RemainingBytes < 2) { SkillDataEvent?.Invoke(System.Array.Empty<int>()); return; }
        int count = p.ReadShort();
        if (count <= 0) { SkillBarClearEvent?.Invoke(); return; }
        var ids = new int[count];
        for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
            ids[i] = p.ReadInt();
        SkillDataEvent?.Invoke(ids);
    }
}
