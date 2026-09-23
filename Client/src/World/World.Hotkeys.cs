using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World : Node3D
{
    private readonly Dictionary<KeyChord, Action> _hotkeys = new();
    private readonly Dictionary<PadChord, Action> _padHotkeys = new();

    private void Bound(KeyAction action, Action handler)
    {
        var chord = KeyBinds.Get(action);
        if (chord.Assigned) _hotkeys[chord] = handler;
        var pad = KeyBinds.GetPad(action);
        if (pad.Assigned) _padHotkeys[pad] = handler;
    }

    private bool _leftTriggerHeld, _rightTriggerHeld;

    private void TickPadTriggers()
    {
        if (!KeyBinds.PadConnected) return;
        FireOnTriggerEdge(PadMod.LeftTrigger, ref _leftTriggerHeld);
        FireOnTriggerEdge(PadMod.RightTrigger, ref _rightTriggerHeld);
    }

    private void FireOnTriggerEdge(PadMod mod, ref bool held)
    {
        bool down = KeyBinds.TriggerHeld(mod);
        if (down && !held
            && _padHotkeys.TryGetValue(new PadChord(JoyButton.Invalid, mod), out var press))
            press();
        held = down;
    }

    private void DevHotkey(Key key, Action handler) => _hotkeys[new KeyChord(key, false, false, false)] = handler;

    private void BuildHotkeys()
    {
        _hotkeys.Clear();
        _padHotkeys.Clear();

        Bound(KeyAction.AutoRun, ToggleAutoRun);
        Bound(KeyAction.ToggleRun, ToggleRunMode);
        Bound(KeyAction.ToggleRunAlt, ToggleRunMode);
        Bound(KeyAction.Sit, () => ToggleSitting());
        Bound(KeyAction.TargetHostile, TargetNearestHostile);
        Bound(KeyAction.TargetFriendly, () => SelectNearest(hostile: false));
        Bound(KeyAction.AutoAttack, () => ToggleAutoAttack());
        Bound(KeyAction.StealthCancel, () => StealthCancelSelf());

        for (int i = 0; i < HotSlotsPerPage; i++)
        {
            int slot = i;
            Bound(KeyAction.HotSlot1 + i, () => ActivateHotSlot(slot));
        }
        for (int i = 0; i < HotPages; i++)
        {
            int page = i;
            Bound(KeyAction.HotPage1 + i, () => SetHotPage(page));
        }
        Bound(KeyAction.HotPageNext, () => ChangeHotPage(1));

        Bound(KeyAction.PotionHp, () => UsePotion(_hpPotion));
        Bound(KeyAction.PotionMp, () => UsePotion(_mpPotion));
        Bound(KeyAction.CameraTurn, StartCameraHalfTurn);
        Bound(KeyAction.GameMenu, HandleEscape);

        Bound(KeyAction.Character, () => ToggleMainWindow("Character"));
        Bound(KeyAction.Inventory, () => ToggleMainWindow("Inventory"));
        Bound(KeyAction.Skills, () => ToggleMainWindow("Skills"));
        Bound(KeyAction.Quests, () => ToggleMainWindow("Quests"));
        Bound(KeyAction.Party, () => ToggleParty());
        Bound(KeyAction.Friends, () => OpenCharacterPage(CharacterPage.Friends));
        Bound(KeyAction.Clan, () => ToggleClan());
        Bound(KeyAction.Messenger, () => ToggleMessenger());
        Bound(KeyAction.MiniMap, () => ToggleMiniMap());
        Bound(KeyAction.ZoneMap, () => ToggleFullMap());
        Bound(KeyAction.WorldMap, () => ToggleGlobalMap());
        Bound(KeyAction.Helmet, () => ToggleHelmet());
        Bound(KeyAction.Interact, () => TryStartGather());
        Bound(KeyAction.MyShop, () => ToggleMerchantMenu());
        Bound(KeyAction.Pet, () => TogglePet());
        Bound(KeyAction.PowerUpStore, ToggleShoppingMall);
        Bound(KeyAction.Rebirth, () => ToggleRebirth());
        Bound(KeyAction.King, () => ToggleKing());
        Bound(KeyAction.Siege, () => ToggleSiege());
        Bound(KeyAction.NameChange, () => ToggleNameChange());
        Bound(KeyAction.ClanWarehouse, () => ToggleClanWarehouse());
        Bound(KeyAction.VipWarehouse, () => ToggleVipWarehouse());
        Bound(KeyAction.Report, () => ToggleReport());
        Bound(KeyAction.Achievements, () => ToggleAchievements());
        Bound(KeyAction.Mail, () => ToggleMail());
        Bound(KeyAction.Lottery, () => ToggleLottery());
        Bound(KeyAction.Auction, () => ToggleAuction());
        Bound(KeyAction.Attendance, () => ToggleAttendance());
        Bound(KeyAction.Bounty, () => ToggleBounty());
        Bound(KeyAction.Tournament, () => ToggleTournament());
        Bound(KeyAction.Disguise, () => ToggleDisguise());
        Bound(KeyAction.Presets, () => TogglePreset());
        Bound(KeyAction.NationForce, () => ToggleForces());
        Bound(KeyAction.Instances, () => ToggleInstance());
        Bound(KeyAction.ChatRooms, () => ToggleChatRoom());
        Bound(KeyAction.Fortune, () => ToggleFortune());
        Bound(KeyAction.ItemCombine, () => ToggleItemCombine());
        Bound(KeyAction.Roulette, () => ToggleRoulette());
        Bound(KeyAction.FishingHall, () => ToggleFishingHall());
        Bound(KeyAction.TradeBoard, () => ToggleMarketBbs());
        Bound(KeyAction.Duel, () => ToggleDuel());
        Bound(KeyAction.ItemExchange, () => ToggleItemExchange());
        Bound(KeyAction.RingUpgrade, () => ToggleRingUpgrade());
        Bound(KeyAction.Inn, () => ToggleInn());
        Bound(KeyAction.EventQuests, () => ToggleEventQuests());
        Bound(KeyAction.Genie, () => ToggleGenie());
        Bound(KeyAction.DailyQuests, () => ToggleDailyQuest());
        Bound(KeyAction.Rentals, () => ToggleRental());
        Bound(KeyAction.TownRecall, () => TownRecallTryOpen());
        Bound(KeyAction.GmPanel, ToggleAdminPanel);

        if (Config.Development)
        {
            DevHotkey(Key.H, ToggleInfoPanel);
            DevHotkey(Key.V, () => PreviewNextFx());
            _hotkeys[new KeyChord(Key.J, true, false, false)] = PreviewFloaters;
        }
    }

    private void PreviewFloaters()
    {
        Floaters.Wound(63);
        Floaters.Cure(_myId, 240);
        Floaters.RegenHp(37);
        Floaters.RegenMp(21);
        Floaters.Exp(4820);
        Floaters.Gold(1350);
        Floaters.Item(379006000, 3);
        Floaters.Notice("You have no Water of Favors left.");
        int victim = _selectedId;
        if (victim < 0)
            foreach (var (id, _) in _ents) { victim = id; break; }
        if (victim >= 0)
            foreach (int hit in new[] { 148, 96, 1207 })
                Floaters.Damage(victim, hit);
    }

    private void ToggleInfoPanel()
    {
        if (_infoPanel == null) return;
        _infoShown = !_infoShown;
        _infoPanel.Visible = _infoShown;
        _infoNextRebuild = 0;
        if (!_infoShown) _camDist = Mathf.Min(_camDist, MaxDist);
        RefreshObjectHighlight();
    }
}
