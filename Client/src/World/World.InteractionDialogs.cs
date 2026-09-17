using System;
using System.Collections.Generic;

namespace LibreKO;

public partial class World
{
    private readonly List<(Func<bool> IsOpen, Action Close)> _interactionDialogs = new();

    private void InteractionCloses(Func<bool> isOpen, Action close) => _interactionDialogs.Add((isOpen, close));

    private void BuildInteractionDialogs()
    {
        InteractionCloses(() => _buyAmountShown, () => CloseBuyAmount());
        InteractionCloses(() => _npcDialogShown, () => CloseNpcDialog());
        InteractionCloses(() => _vendorShown, () => CloseVendor());
        InteractionCloses(() => _repairShown, () => CloseRepair());
        InteractionCloses(() => _whShown, () => CloseWarehouse());
        InteractionCloses(() => _clanWhShown, () => CloseClanWarehouse());
        InteractionCloses(() => _vipWhShown, () => CloseVipWarehouse());
        InteractionCloses(() => _warpShown, () => CloseWarp());
        InteractionCloses(() => _upgradeShown, () => CloseUpgrade());
        InteractionCloses(() => _pieceShown, () => ClosePieceChange());
        InteractionCloses(() => _classChangeShown, () => CloseClassChange());
        InteractionCloses(() => _capeShown, () => CloseCape());
        InteractionCloses(() => _kingShown, () => CloseKing());
        InteractionCloses(() => _rentShown, () => CloseRental());
    }

    public bool InteractionDialogOpen => AnyInteractionDialogShown();

    private bool AnyInteractionDialogShown()
    {
        foreach (var (isOpen, _) in _interactionDialogs)
            if (isOpen()) return true;
        return false;
    }

    private void CloseInteractionDialogs()
    {
        bool closed = false;
        foreach (var (isOpen, close) in _interactionDialogs)
            if (isOpen()) { close(); closed = true; }
        if (closed) _npcTalkId = -1;
    }

    private void StopForInteraction()
    {
        _hasMoveTarget = false;
        _terrainMoveHeld = false;
        _autoMoveForward = false;
        StopAutoAttack();
    }
}
