using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ResetConfirmFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly DevelopSession develop;
    private readonly IDevelopRequests developRequests;

    public ResetConfirmFeature(IWorldContext context, DevelopSession develop, IDevelopRequests developRequests)
    {
        this.context = context;
        this.develop = develop;
        this.developRequests = developRequests;
    }

    private const int TextResetNeedsCoins = 6086;
    private const int TextResetNothing = 6087;
    private const int TextResetNeedsFirstJob = 6090;
    private const int TextResetItemsEquipped = 6112;
    private const int TextResetDone = 12903;

    private readonly RedistributionRequest reset = new();
    private Notice? resetConfirm;

    internal static string ResetItemsEquippedText => ItemData.Text(
        TextResetItemsEquipped, "You cannot change your stat while there are items equipped on you.");

    public void Build()
    {
        develop.ResetCostEvent += OnResetCost;
        develop.StatResetEvent += OnStatResetFinished;
        develop.SkillResetEvent += OnSkillResetFinished;
        develop.ResetRefusedEvent += OnResetRefused;
    }

    public void Dispose()
    {
        develop.ResetCostEvent -= OnResetCost;
        develop.StatResetEvent -= OnStatResetFinished;
        develop.SkillResetEvent -= OnSkillResetFinished;
        develop.ResetRefusedEvent -= OnResetRefused;
        Dismiss();
    }

    internal void RequestReset(ResetKind kind)
    {
        if (!reset.TryBegin((byte)kind)) return;
        developRequests.ResetCostQuery(kind);
    }

    private void OnResetCost(int cost)
    {
        if (!reset.TakeCost()) return;
        Dismiss();

        bool stat = reset.Kind == ResetKind.Stat;
        string what = stat ? "stat points" : "mastery points";
        string body = $"Every one of your {what} goes back into the pool, and it costs "
                      + $"{cost:n0} gold.";
        if (stat)
            body += "\n\n" + ResetItemsEquippedText;

        resetConfirm = Notice.Confirm(
            context.Root,
            body,
            "Redistribute",
            "Cancel",
            ConfirmReset,
            CancelReset,
            stat ? "Redistribute stats" : "Redistribute mastery");
    }

    private void ConfirmReset()
    {
        if (!reset.CanConfirm) return;
        resetConfirm = null;
        if (reset.Kind == ResetKind.Stat) developRequests.StatReset();
        else developRequests.SkillReset();
    }

    private void CancelReset()
    {
        resetConfirm = null;
        reset.Clear();
    }

    private void Dismiss()
    {
        if (resetConfirm != null && GodotObject.IsInstanceValid(resetConfirm))
            resetConfirm.Close();
        resetConfirm = null;
    }

    private void OnStatResetFinished(StatResetReply reply)
    {
        if (reply.Succeeded) context.Log.Notice(ItemData.Text(TextResetDone, "Redistribution successful."));
        CancelReset();
    }

    private void OnSkillResetFinished(bool ok, int money, int pool)
    {
        if (ok) context.Log.Notice(ItemData.Text(TextResetDone, "Redistribution successful."));
        CancelReset();
    }

    private void OnResetRefused(ResetResult result, int cost)
    {
        string text = result switch
        {
            ResetResult.Refused => ItemData.Text(TextResetNeedsCoins, "You need %d Coins")
                .Replace("%d", cost.ToString("n0")),
            ResetResult.NothingToReset => ItemData.Text(TextResetNothing, "There are no points to reset."),
            ResetResult.NeedsFirstJob => ItemData.Text(TextResetNeedsFirstJob, "You must first undergo the 1st job change."),
            ResetResult.InventoryNotEmpty => ResetItemsEquippedText,
            _ => "",
        };
        if (text.Length > 0) context.Log.Notice(text);
    }
}
