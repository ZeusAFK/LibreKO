namespace LibreKO.Session;

public sealed class DevelopSession
{
    public bool RebirthPending { get; internal set; }

    public event Action<int, int>? SkillPointChangeEvent;

    internal void RaiseSkillPointChange(int arg1, int arg2) => SkillPointChangeEvent?.Invoke(arg1, arg2);

    public event Action<bool, int, int>? SkillResetEvent;

    internal void RaiseSkillReset(bool arg1, int arg2, int arg3) => SkillResetEvent?.Invoke(arg1, arg2, arg3);

    public event Action<ResetResult, int>? ResetRefusedEvent;

    internal void RaiseResetRefused(ResetResult result, int cost) => ResetRefusedEvent?.Invoke(result, cost);

    public event Action<StatResetReply>? StatResetEvent;

    internal void RaiseStatReset(StatResetReply reply) => StatResetEvent?.Invoke(reply);

    public event Action? ClassChangeNpcEvent;

    internal void RaiseClassChangeNpc() => ClassChangeNpcEvent?.Invoke();

    public event Action<JobChangeEligibility>? ClassEligibilityEvent;

    internal void RaiseClassEligibility(JobChangeEligibility eligibility) => ClassEligibilityEvent?.Invoke(eligibility);

    public event Action<JobChangeOutcome>? JobChangeResultEvent;

    internal void RaiseJobChangeResult(JobChangeOutcome outcome) => JobChangeResultEvent?.Invoke(outcome);

    public event Action<int, int>? ClassPromotedEvent;

    internal void RaiseClassPromoted(int arg1, int arg2) => ClassPromotedEvent?.Invoke(arg1, arg2);

    public event Action<ClassChangeSubOpcode, int>? RebStatChangeEvent;

    internal void RaiseRebStatChange(ClassChangeSubOpcode arg1, int arg2) => RebStatChangeEvent?.Invoke(arg1, arg2);

    public event Action? RebirthResetEvent;

    private void RaiseRebirthReset() => RebirthResetEvent?.Invoke();

    public event Action<int>? ResetCostEvent;

    internal void RaiseResetCost(int value) => ResetCostEvent?.Invoke(value);

    internal void ApplyRebirthStatChange(ClassChangeSubOpcode sub, int result)
    {
        RebirthPending = false;
        RaiseRebStatChange(sub, result);
    }

    internal void ApplyRebirthReset()
    {
        RebirthPending = false;
        RaiseRebirthReset();
    }
}
