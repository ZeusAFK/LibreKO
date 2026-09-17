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
        EscapeCloses(() => _buyAmountShown, () => CloseBuyAmount());
        EscapeCloses(() => _questNotifications.Count > 0, () => AnswerQuestNotification(-1));
        EscapeCloses(() => _npcDialogShown, () => CloseNpcDialog());
        EscapeCloses(() => _vendorShown, () => CloseVendor());
        EscapeCloses(() => _repairShown, () => CloseRepair());
        EscapeCloses(() => _whShown, () => CloseWarehouse());
        EscapeCloses(() => _upgradeShown, () => CloseUpgrade());
        EscapeCloses(() => _classChangeShown, () => CloseClassChange());
        EscapeCloses(() => _exAmountShown, () => CloseExchangeAmount());
        EscapeCloses(() => _exWaiting, () => CancelExchangeRequest());
        EscapeCloses(() => _exShown, () => AbortExchange(local: true));
        EscapeCloses(() => _shopShown, () => CloseShop());
        EscapeCloses(() => _amountLayer.Visible, CloseAmountPrompt);
        EscapeCloses(() => _wishFindShown, CloseWishFind);
        EscapeCloses(() => _wishShown, CloseWishList);
        EscapeCloses(() => _wantedShown, CloseWantedStall);
        EscapeCloses(() => _sellStallShown, CloseSellStall);
        EscapeCloses(() => _merchantMenuShown, CloseMerchantMenu);
        EscapeCloses(() => _clanShown, () => CloseClan());
        EscapeCloses(() => _warpShown, () => CloseWarp());
        EscapeCloses(() => _rankShown, () => ToggleRank());
        EscapeCloses(() => _petShown, () => TogglePet());
        EscapeCloses(() => _shoppingmallShown, () => ToggleShoppingMall());
        EscapeCloses(() => _rebirthShown, () => CloseRebirth());
        EscapeCloses(() => _kingShown, () => CloseKing());
        EscapeCloses(() => _siegeShown, () => CloseSiege());
        EscapeCloses(() => _capeShown, () => CloseCape());
        EscapeCloses(() => _clanWhShown, () => CloseClanWarehouse());
        EscapeCloses(() => _vipWhShown, () => CloseVipWarehouse());
        EscapeCloses(() => _reportShown, () => CloseReport());
        EscapeCloses(() => _changeHairShown, () => CloseChangeHair());
        EscapeCloses(() => _achShown, () => CloseAchievements());
        EscapeCloses(() => _mailShown, () => CloseMail());
        EscapeCloses(() => _auctionShown, () => CloseAuction());
        EscapeCloses(() => _attendanceShown, () => CloseAttendance());
        EscapeCloses(() => _bountyShown, () => CloseBounty());
        EscapeCloses(() => _tournamentShown, () => CloseTournament());
        EscapeCloses(() => _disguiseShown, () => CloseDisguise());
        EscapeCloses(() => _presetShown, () => ClosePreset());
        EscapeCloses(() => _msgrShown, () => CloseMessenger());
        EscapeCloses(() => _forcesShown, () => CloseForces());
        EscapeCloses(() => _instanceShown, () => CloseInstance());
        EscapeCloses(() => _chatRoomShown, () => CloseChatRoom());
        EscapeCloses(() => _fortuneShown, () => CloseFortune());
        EscapeCloses(() => _itemCombineShown, () => CloseItemCombine());
        EscapeCloses(() => _rouletteShown, () => CloseRoulette());
        EscapeCloses(() => _fishHallShown, () => CloseFishingHall());
        EscapeCloses(() => _mbbsShown, () => CloseMarketBbs());
        EscapeCloses(() => _duelShown, () => CloseDuel());
        EscapeCloses(() => _itemExchangeShown, () => CloseItemExchange());
        EscapeCloses(() => _ringUpShown, () => CloseRingUpgrade());
        EscapeCloses(() => _innShown, () => CloseInn());
        EscapeCloses(() => _eventQuestShown, () => CloseEventQuests());
        EscapeCloses(() => _globalMapShown, () => CloseGlobalMap());
        EscapeCloses(() => _genieShown, () => CloseGenie());
        EscapeCloses(() => _dqShown, () => CloseDailyQuest());
        EscapeCloses(() => _rentShown, () => CloseRental());
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
