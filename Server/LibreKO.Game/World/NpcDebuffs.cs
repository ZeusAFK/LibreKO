using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public readonly record struct NpcDebuff(
    int SkillId,
    long ExpireTicks,
    short SpeedPercent,
    short Ac,
    short AcPercent,
    short AttackPercent);

public sealed class NpcDebuffs
{
    private const int NeutralPercent = 100;

    private readonly ConcurrentDictionary<BuffType, NpcDebuff> _active = new();

    public static bool Affects(BuffType buffType) =>
        buffType is BuffType.Speed or BuffType.Speed2 or BuffType.Stun or BuffType.Ac or BuffType.Damage;

    public bool TryApply(int skillId, MagicType4Data type4, long nowTicks)
    {
        var buffType = (BuffType)type4.BuffType;
        if (!Affects(buffType) || type4.Duration <= 0)
            return false;

        _active[buffType] = new NpcDebuff(
            skillId,
            nowTicks + type4.Duration * TimeSpan.TicksPerSecond,
            type4.Speed,
            type4.Ac,
            type4.AcPct,
            type4.Attack);
        return true;
    }

    public bool Has(BuffType buffType, long nowTicks) =>
        _active.TryGetValue(buffType, out var debuff) && debuff.ExpireTicks > nowTicks;

    public bool IsStunned(long nowTicks) => Has(BuffType.Stun, nowTicks);

    public float SpeedFactor(long nowTicks)
    {
        var factor = 1f;
        foreach (var buffType in (ReadOnlySpan<BuffType>)[BuffType.Speed, BuffType.Speed2])
            if (Live(buffType, nowTicks) is { SpeedPercent: > 0 } slow)
                factor *= slow.SpeedPercent / (float)NeutralPercent;
        return factor;
    }

    public int ScaleAc(int ac, long nowTicks)
    {
        if (Live(BuffType.Ac, nowTicks) is not { } debuff)
            return ac;

        return debuff.Ac == 0 && debuff.AcPercent > 0
            ? ac * debuff.AcPercent / NeutralPercent
            : ac + debuff.Ac;
    }

    public int ScaleAttack(int attack, long nowTicks) =>
        Live(BuffType.Damage, nowTicks) is { AttackPercent: > 0 } debuff
            ? attack * debuff.AttackPercent / NeutralPercent
            : attack;

    public void Clear() => _active.Clear();

    private NpcDebuff? Live(BuffType buffType, long nowTicks)
    {
        if (!_active.TryGetValue(buffType, out var debuff))
            return null;

        if (debuff.ExpireTicks > nowTicks)
            return debuff;

        _active.TryRemove(new KeyValuePair<BuffType, NpcDebuff>(buffType, debuff));
        return null;
    }
}
