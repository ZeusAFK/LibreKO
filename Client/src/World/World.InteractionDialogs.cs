using System;
using System.Collections.Generic;

namespace LibreKO;

public partial class World
{
    private readonly List<(Func<bool> IsOpen, Action Close)> _interactionDialogs = new();

    private void InteractionCloses(Func<bool> isOpen, Action close) => _interactionDialogs.Add((isOpen, close));

    private void BuildInteractionDialogs()
    {
        InteractionCloses(() => _npcDialogShown, () => CloseNpcDialog());
        InteractionCloses(() => _vendorShown, () => CloseVendor());
        InteractionCloses(() => _repairShown, () => CloseRepair());
        InteractionCloses(() => _whShown, () => CloseWarehouse());
        InteractionCloses(() => _clanWhShown, () => CloseClanWarehouse());
        InteractionCloses(() => _vipWhShown, () => CloseVipWarehouse());
        InteractionCloses(() => _warpShown, () => CloseWarp());
        InteractionCloses(() => _upgradeShown, () => CloseUpgrade());
        InteractionCloses(() => _itemCombineShown, () => CloseItemCombine());
        InteractionCloses(() => _pieceShown, () => ClosePieceChange());
        InteractionCloses(() => _classChangeShown, () => CloseClassChange());
        InteractionCloses(() => _capeShown, () => CloseCape());
        InteractionCloses(() => _kingElectionShown, CloseKingElection);
        InteractionCloses(() => _kingNominateShown, CloseKingNominate);
        InteractionCloses(() => _kingPlanShown, CloseKingPlanEditor);
        InteractionCloses(() => _kingVoteShown, CloseKingVote);
        InteractionCloses(() => _nationTaxShown, CloseNationTax);
        InteractionCloses(() => _nationTaxRateShown, CloseNationTaxRate);
        InteractionCloses(() => _nationIntroShown, CloseNationIntro);
        InteractionCloses(() => _siegeGuardShown, CloseSiegeGuard);
        InteractionCloses(() => _siegeScheduleShown, CloseSiegeSchedule);
        InteractionCloses(() => _siegeChallengersShown, CloseSiegeChallengers);
        InteractionCloses(() => _siegeDefendersShown, CloseSiegeDefenders);
        InteractionCloses(() => _siegeOfficeShown, CloseSiegeOffice);
        InteractionCloses(() => _siegeTaxListShown, CloseSiegeTaxList);
        InteractionCloses(() => _siegeTaxRateShown, CloseSiegeTaxRate);
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
