using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private byte _resetKind;
    private bool _resetThenApplyPlan;
    private Notice? _resetConfirm;

    private void ResetConfirmInit()
    {
        Net.I.ResetCostEvent += OnResetCost;
        Net.I.StatResetEvent += OnStatResetFinished;
        Net.I.SkillResetEvent += OnSkillResetFinished;
    }

    private void ResetConfirmDispose()
    {
        Net.I.ResetCostEvent -= OnResetCost;
        Net.I.StatResetEvent -= OnStatResetFinished;
        Net.I.SkillResetEvent -= OnSkillResetFinished;
        DismissResetConfirm();
    }

    private void RequestReset(byte kind, bool thenApplyPlan = false)
    {
        _resetKind = kind;
        _resetThenApplyPlan = thenApplyPlan;
        Net.I.SendResetCostQuery(kind);
    }

    private void OnResetCost(int cost)
    {
        if (_resetKind == 0) return;
        DismissResetConfirm();

        bool stat = _resetKind == Net.ResetKindStat;
        string what = stat ? "stat points" : "mastery points";
        string body = $"Every one of your {what} goes back into the pool, and it costs "
                      + $"{cost:n0} gold.";
        if (_resetThenApplyPlan)
            body += " Your saved plan is applied straight afterwards.";
        if (stat)
            body += "\n\nYour inventory must be empty.";

        _resetConfirm = Notice.Confirm(
            this,
            body,
            _resetThenApplyPlan ? "Redistribute and apply" : "Redistribute",
            "Cancel",
            ConfirmReset,
            CancelReset,
            stat ? "Redistribute stats" : "Redistribute mastery");
    }

    private void ConfirmReset()
    {
        _resetConfirm = null;
        if (_resetKind == Net.ResetKindStat) Net.I.SendStatReset();
        else Net.I.SendSkillReset();
    }

    private void CancelReset()
    {
        _resetConfirm = null;
        _resetKind = 0;
        _resetThenApplyPlan = false;
    }

    private void DismissResetConfirm()
    {
        if (_resetConfirm != null && GodotObject.IsInstanceValid(_resetConfirm))
            _resetConfirm.Close();
        _resetConfirm = null;
    }

    private void OnStatResetFinished(
        bool ok, int money, int[] stats, int maxHp, int maxMp, int ap, int statPoints)
    {
        if (!ok)
        {
            CombatNotice(money > 0
                ? $"The redistribution needs {money:n0} gold, and an empty inventory."
                : "There is nothing to redistribute.");
            CancelReset();
            return;
        }

        bool apply = _resetThenApplyPlan && _resetKind == Net.ResetKindStat;
        CancelReset();
        if (apply) Callable.From(SendStatPlan).CallDeferred();
    }

    private void OnSkillResetFinished(bool ok, int money, int pool)
    {
        if (!ok)
        {
            CombatNotice(money > 0
                ? $"The redistribution needs {money:n0} gold."
                : "There is nothing to redistribute.");
            CancelReset();
            return;
        }

        bool apply = _resetThenApplyPlan && _resetKind == Net.ResetKindSkill;
        CancelReset();
        if (apply) Callable.From(SendSkillPlan).CallDeferred();
    }
}
