using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private readonly RedistributionRequest _reset = new();
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

    private void RequestReset(byte kind)
    {
        if (!_reset.TryBegin(kind)) return;
        Net.I.SendResetCostQuery(kind);
    }

    private void OnResetCost(int cost)
    {
        if (!_reset.TakeCost()) return;
        DismissResetConfirm();

        bool stat = _reset.Kind == Net.ResetKindStat;
        string what = stat ? "stat points" : "mastery points";
        string body = $"Every one of your {what} goes back into the pool, and it costs "
                      + $"{cost:n0} gold.";
        if (stat)
            body += "\n\nUnequip every item first.";

        _resetConfirm = Notice.Confirm(
            this,
            body,
            "Redistribute",
            "Cancel",
            ConfirmReset,
            CancelReset,
            stat ? "Redistribute stats" : "Redistribute mastery");
    }

    private void ConfirmReset()
    {
        if (!_reset.CanConfirm) return;
        _resetConfirm = null;
        if (_reset.Kind == Net.ResetKindStat) Net.I.SendStatReset();
        else Net.I.SendSkillReset();
    }

    private void CancelReset()
    {
        _resetConfirm = null;
        _reset.Clear();
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
                ? $"The redistribution needs {money:n0} gold, and empty equipment slots."
                : "There is nothing to redistribute.");
            CancelReset();
            return;
        }

        CancelReset();
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

        CancelReset();
    }
}
