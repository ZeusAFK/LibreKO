namespace LibreKO.Features.Windows;

internal sealed class BagCompanionFeature : IFeature, IBagCompanions
{
    private readonly IWorldContext context;
    private readonly SelfSession self;

    public BagCompanionFeature(IWorldContext context, SelfSession self)
    {
        this.context = context;
        this.self = self;
    }

    public void Dispose()
    {
    }

    private const string ItemInUseText = "That item is in use.";

    public bool CompanionInto(int from, int to, int? amount) => Current?.IntoBag?.Invoke(from, to, amount) ?? false;

    public BagCompanion? Current { get; private set; }

    internal BagPairing<BagCompanion> Pairing { get; } = new();

    public SlotHold Hold { get; } = new();

    public void Build()
    {
    }

    public void Attach(BagCompanion companion)
    {
        var previous = Pairing.Attach(companion, context.MainWindows.CharTabOpen());
        Current = companion;
        context.Inventory.RefreshArrangeButton();
        previous?.Close();
        if (context.MainWindows.CharTabOpen()) ApplyFit();
        else context.MainWindows.Show("Inventory");
    }

    public void Detach(BagCompanion companion)
    {
        if (Current != companion) return;
        Current = null;
        context.Inventory.RefreshArrangeButton();
        if (Pairing.Detach(companion)) context.MainWindows.Hide("Inventory");
        else if (context.MainWindows.CharTabOpen()) ApplyFit();
    }

    public void RefreshFit()
    {
        if (context.MainWindows.CharTabOpen()) ApplyFit();
    }

    public bool RefuseItemInUse(int abs, int other = -1)
    {
        if (FitAt(abs) != BagFit.Staged && FitAt(other) != BagFit.Staged) return false;
        context.Log.Notice(ItemInUseText);
        return true;
    }

    public bool IsStagedSlot(int abs) => FitAt(abs) == BagFit.Staged;

    private BagFit FitAt(int abs) =>
        Current == null || !ItemMove.IsCarried(abs) || abs >= self.Inventory.Length || self.Inventory[abs].IsEmpty
            ? BagFit.Normal
            : Current.Fit(abs);

    public void ReleaseHold()
    {
        if (!Hold.Active) return;
        Hold.Release();
        if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
    }

    public void ApplyFit()
    {
        foreach (var (slot, cell) in context.Inventory.Cells) cell.Fit = FitAt(slot);
        for (int i = 0; i < context.Inventory.BagCells.Count; i++) context.Inventory.BagCells[i].Fit = FitAt(Inventory.GridStart + i);
        for (int i = 0; i < context.Inventory.MagicBagCells.Count; i++)
            context.Inventory.MagicBagCells[i].Fit = FitAt(InventoryConstants.MagicBagStart + i);
    }
}
