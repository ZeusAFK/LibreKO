using System;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const string ItemInUseText = "That item is in use.";

    private enum BagFit { Normal, Unfit, Staged }

    private sealed record BagCompanion(
        Func<int, bool> Take,
        Func<int, BagFit> Fit,
        Func<int, string> Note,
        Action Close,
        Func<int, int, bool>? IntoBag = null);

    private bool CompanionIntoBag(int from, int to) => _bagCompanion?.IntoBag?.Invoke(from, to) ?? false;

    private BagCompanion? _bagCompanion;
    private readonly BagPairing<BagCompanion> _bagPairing = new();
    private readonly SlotHold _bagHold = new();

    private void BagCompanionInit()
    {
        if (_mainWindows.TryGetValue("Inventory", out var inventory))
            inventory.Layout.Placed += _bagPairing.PlayerTouched;
    }

    private void AttachBagCompanion(BagCompanion companion)
    {
        var previous = _bagPairing.Attach(companion, CharTabOpen());
        _bagCompanion = companion;
        previous?.Close();
        if (CharTabOpen()) ApplyBagFit();
        else ShowMainWindow("Inventory");
    }

    private void DetachBagCompanion(BagCompanion companion)
    {
        if (_bagCompanion != companion) return;
        _bagCompanion = null;
        if (_bagPairing.Detach(companion)) HideMainWindow("Inventory");
        else if (CharTabOpen()) ApplyBagFit();
    }

    private void RefreshBagFit()
    {
        if (CharTabOpen()) ApplyBagFit();
    }

    private bool RefuseItemInUse(int abs, int other = -1)
    {
        if (BagFitAt(abs) != BagFit.Staged && BagFitAt(other) != BagFit.Staged) return false;
        CombatNotice(ItemInUseText);
        return true;
    }

    private bool IsStagedSlot(int abs) => BagFitAt(abs) == BagFit.Staged;

    private BagFit BagFitAt(int abs) =>
        _bagCompanion == null || !ItemMove.IsCarried(abs) || abs >= Inv.Length || Inv[abs].IsEmpty
            ? BagFit.Normal
            : _bagCompanion.Fit(abs);

    private void ReleaseBagHold()
    {
        if (!_bagHold.Active) return;
        _bagHold.Release();
        if (CharTabOpen()) RefreshInventoryUI();
    }

    private void ApplyBagFit()
    {
        foreach (var (slot, cell) in _invCells) cell.Fit = BagFitAt(slot);
        for (int i = 0; i < _invBagCells.Count; i++) _invBagCells[i].Fit = BagFitAt(GridStart + i);
        for (int i = 0; i < _invMagicBagCells.Count; i++)
            _invMagicBagCells[i].Fit = BagFitAt(InventoryConstants.MagicBagStart + i);
    }
}
