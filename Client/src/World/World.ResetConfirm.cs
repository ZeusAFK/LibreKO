using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const byte ResetRefusedNeedsCoins = 0;
    private const byte ResetRefusedNothingToReset = 2;
    private const byte ResetRefusedItemsEquipped = 4;
    private const int TextResetNeedsCoins = 6086;
    private const int TextResetNothing = 6087;
    private const int TextResetItemsEquipped = 6112;

    private readonly RedistributionRequest _reset = new();
    private Notice? _resetConfirm;

    private static string ResetItemsEquippedText => ItemData.Text(
        TextResetItemsEquipped, "You cannot change your stat while there are items equipped on you.");

    private void ResetConfirmInit()
    {
        Net.I.ResetCostEvent += OnResetCost;
        Net.I.StatResetEvent += OnStatResetFinished;
        Net.I.SkillResetEvent += OnSkillResetFinished;
        Net.I.ResetRefusedEvent += OnResetRefused;
    }

    private void ResetConfirmDispose()
    {
        Net.I.ResetCostEvent -= OnResetCost;
        Net.I.StatResetEvent -= OnStatResetFinished;
        Net.I.SkillResetEvent -= OnSkillResetFinished;
        Net.I.ResetRefusedEvent -= OnResetRefused;
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
            body += "\n\n" + ResetItemsEquippedText;

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
        bool ok, int money, int[] stats, int maxHp, int maxMp, int ap, int statPoints) => CancelReset();

    private void OnSkillResetFinished(bool ok, int money, int pool) => CancelReset();

    private void OnResetRefused(byte result, int cost)
    {
        string text = result switch
        {
            ResetRefusedNeedsCoins => ItemData.Text(TextResetNeedsCoins, "You need %d Coins")
                .Replace("%d", cost.ToString("n0")),
            ResetRefusedNothingToReset => ItemData.Text(TextResetNothing, "There are no points to reset."),
            ResetRefusedItemsEquipped => ResetItemsEquippedText,
            _ => "",
        };
        if (text.Length > 0) CombatNotice(text);
    }
}
