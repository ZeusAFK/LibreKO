using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

// Order is precedence, not style: the first open entry wins. Insert deliberately, not at the end.
public partial class World : Node3D
{
    private readonly List<(Func<bool> IsOpen, Action Close)> _escapeStack = new();

    private void EscapeCloses(Func<bool> isOpen, Action close) => _escapeStack.Add((isOpen, close));

    private void BuildEscapeStack()
    {
        EscapeCloses(() => AimingAreaSkill, () => CancelAreaCast());
        EscapeCloses(() => _fullMapShown, () => ToggleFullMap());
        EscapeCloses(() => _questNotifications.Count > 0, () => DismissQuestNotifications());
        EscapeCloses(() => _npcDialogShown, () => CloseNpcDialog());
        EscapeCloses(() => _tradePrompt.Visible, () => _tradePrompt.Close());
        EscapeCloses(() => _vendorShown, () => CloseVendor());
        EscapeCloses(() => _repairShown, () => CloseRepair());
        EscapeCloses(() => _whAmount.Visible, () => _whAmount.Close());
        EscapeCloses(() => _whShown, () => CloseWarehouse());
        EscapeCloses(() => _upgradeShown, () => CloseUpgrade());
        EscapeCloses(() => _sealShown, CloseSealWindow);
        EscapeCloses(() => _pieceShown, ClosePieceChange);
        EscapeCloses(() => _classChangeShown, () => CloseClassChange());
        EscapeCloses(() => _genderShown, CloseGenderChange);
        EscapeCloses(() => _transferShown, CancelNationTransfer);
        EscapeCloses(() => _nameChangeShown, CloseNameChange);
        EscapeCloses(() => _exAmountShown, () => CloseExchangeAmount());
        EscapeCloses(() => _exWaiting, () => CancelExchangeRequest());
        EscapeCloses(() => _exShown, () => AbortExchange(local: true));
        EscapeCloses(() => _marketPriceShown, CloseMarketPrice);
        EscapeCloses(() => _amountLayer.Visible, CloseAmountPrompt);
        EscapeCloses(() => _shopShown, () => CloseShop());
        EscapeCloses(() => _merchantSearchShown, CloseMerchantSearch);
        EscapeCloses(() => _wishFindShown, CloseWishFind);
        EscapeCloses(() => _wishShown, CloseWishList);
        EscapeCloses(() => _wantedShown, CloseWantedStall);
        EscapeCloses(() => _sellStallShown, CloseSellStall);
        EscapeCloses(() => _merchantMenuShown, CloseMerchantMenu);
        EscapeCloses(() => _clanCreateShown, () => CloseClanCreate());
        EscapeCloses(() => _clanPointsShown, () => CloseClanPoints());
        EscapeCloses(() => _warpShown, () => CloseWarp());
        EscapeCloses(() => _rankShown, () => ToggleRank());
        EscapeCloses(() => _petHatchShown, ClosePetHatch);
        EscapeCloses(() => _petShown, () => TogglePet());
        EscapeCloses(() => _pusShown && _pusModal != PusModal.None, ClosePusModal);
        EscapeCloses(() => _pusShown, CloseShoppingMall);
        EscapeCloses(() => _rebirthShown, () => CloseRebirth());
        EscapeCloses(() => _admSpawnShown, CloseAdminSpawn);
        EscapeCloses(() => _kingBallotShown, () => PressKingBallot(null));
        EscapeCloses(() => _nationTaxRateShown, CloseNationTaxRate);
        EscapeCloses(() => _nationIntroShown, CloseNationIntro);
        EscapeCloses(() => _nationTaxShown, CloseNationTax);
        EscapeCloses(() => _kingVoteShown, CloseKingVote);
        EscapeCloses(() => _kingPlanShown, CloseKingPlanEditor);
        EscapeCloses(() => _kingNominateShown, CloseKingNominate);
        EscapeCloses(() => _kingElectionShown, CloseKingElection);
        EscapeCloses(() => _siegeTaxRateShown, CloseSiegeTaxRate);
        EscapeCloses(() => _siegeTaxListShown, CloseSiegeTaxList);
        EscapeCloses(() => _siegeOfficeShown, CloseSiegeOffice);
        EscapeCloses(() => _siegeDefendersShown, CloseSiegeDefenders);
        EscapeCloses(() => _siegeChallengersShown, CloseSiegeChallengers);
        EscapeCloses(() => _siegeScheduleShown, CloseSiegeSchedule);
        EscapeCloses(() => _siegeGuardShown, CloseSiegeGuard);
        EscapeCloses(() => _capeShown, () => CloseCape());
        EscapeCloses(() => _clanWhShown, () => CloseClanWarehouse());
        EscapeCloses(() => _vipWhShown, () => CloseVipWarehouse());
        EscapeCloses(() => _reportShown, () => CloseReport());
        EscapeCloses(() => _equipViewShown, CloseEquipView);
        EscapeCloses(() => _changeHairShown, () => CloseChangeHair());
        EscapeCloses(() => _achShown, () => CloseAchievements());
        EscapeCloses(() => _mailComposeShown, HideMailCompose);
        EscapeCloses(() => _mailReadWindow is { Visible: true }, CloseMailRead);
        EscapeCloses(() => _mailShown, () => CloseMail());
        EscapeCloses(() => _lotteryShown, () => CloseLottery());
        EscapeCloses(() => _specialAuctionShown, () => CloseSpecialAuction());
        EscapeCloses(() => _attendanceShown, () => CloseAttendance());
        EscapeCloses(() => _bountyShown, () => CloseBounty());
        EscapeCloses(() => _tournamentShown, () => CloseTournament());
        EscapeCloses(() => _disguiseShown, () => CloseDisguise());
        EscapeCloses(() => _presetShown, () => ClosePreset());
        EscapeCloses(() => _titleShown, CloseTitlePicker);
        EscapeCloses(() => _chatColorsWindow is { Visible: true }, CloseChatColors);
        EscapeCloses(() => _msgrShown, () => CloseMessenger());
        EscapeCloses(() => _forcesShown, () => CloseForces());
        EscapeCloses(() => _chatRoomShown, () => CloseChatRoom());
        EscapeCloses(() => _fortuneShown, () => CloseFortune());
        EscapeCloses(() => _combineBookShown, () => CloseCombineRecipeBook());
        EscapeCloses(() => _itemCombineShown, () => CloseItemCombine());
        EscapeCloses(() => _rouletteShown, () => CloseRoulette());
        EscapeCloses(() => _fishHallShown, () => CloseFishingHall());
        EscapeCloses(() => _globalMapShown, () => CloseGlobalMap());
        EscapeCloses(() => _genieShown, () => CloseGenie());
        EscapeCloses(() => _admShown, () => CloseAdminPanel());
        EscapeCloses(() => _mainShown, () => SetMainShown(false));
        EscapeCloses(AnyWhisperExpanded, MinimizeAllWhispers);
    }

    private void HandleEscape()
    {
        if (_deathLayer is { Visible: true }) { ToggleEsc(); return; }

        foreach (var (isOpen, close) in _escapeStack)
            if (isOpen()) { close(); return; }
        ToggleEsc();
    }
}
