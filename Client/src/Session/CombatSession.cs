namespace LibreKO.Session;

public sealed class CombatSession
{
    public event System.Action<int>? BuffExpiredEvent;

    internal void RaiseBuffExpired(int value) => BuffExpiredEvent?.Invoke(value);

    public event System.Action<int, System.Collections.Generic.IReadOnlyList<TargetQuestDrop>>? TargetQuestDropsEvent;

    internal void RaiseTargetQuestDrops(int arg1, System.Collections.Generic.IReadOnlyList<TargetQuestDrop> iReadOnlyList) => TargetQuestDropsEvent?.Invoke(arg1, iReadOnlyList);

    public event System.Action<int>? BuffExtendedEvent;

    internal void RaiseBuffExtended(int value) => BuffExtendedEvent?.Invoke(value);

    public event Action<int, int>? ResurrectOfferEvent;

    internal void RaiseResurrectOffer(int arg1, int arg2) => ResurrectOfferEvent?.Invoke(arg1, arg2);

    public event Action<RespawnRefusal, int, ushort>? RegeneRefusedEvent;

    internal void RaiseRegeneRefused(RespawnRefusal arg1, int arg2, ushort arg3) => RegeneRefusedEvent?.Invoke(arg1, arg2, arg3);

    public int[]? SkillBar { get; private set; }

    public bool SkillBarRequested { get; private set; }

    internal void MarkSkillBarRequested() => SkillBarRequested = true;

    internal void ForgetSkillBar()
    {
        SkillBarRequested = false;
        SkillBar = null;
    }

    public event Action<AttackReply>? AttackEvent;

    internal void RaiseAttack(AttackReply reply) => AttackEvent?.Invoke(reply);

    public event Action<MagicReply>? MagicEvent;

    internal void RaiseMagic(MagicReply reply) => MagicEvent?.Invoke(reply);

    public event Action<int, int>? DeadEvent;

    internal void RaiseDead(int arg1, int arg2) => DeadEvent?.Invoke(arg1, arg2);

    public event Action<int[]>? SkillDataEvent;

    internal void RaiseSkillData(int[] value) => SkillDataEvent?.Invoke(value);

    public event Action? SkillBarClearEvent;

    private void RaiseSkillBarClear() => SkillBarClearEvent?.Invoke();

    internal void ClearSkillBar()
    {
        SkillBar = Array.Empty<int>();
        RaiseSkillBarClear();
    }

    internal void ApplySkillBar(int[] ids)
    {
        SkillBar = ids;
        RaiseSkillData(ids);
    }

    internal void KeepSkillBar(int[] ids) => SkillBar = ids;
}
