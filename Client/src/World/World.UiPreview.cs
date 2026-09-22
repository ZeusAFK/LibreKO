using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int PreviewPotion = 389070000;
    private const int PreviewHpPotion = 389015000;
    private const int PreviewMpPotion = 389020000;
    private const int PreviewPotionCount = 63;
    private const int PreviewUpgradedWeapon = 156210008;
    private const int PreviewUpgradedResult = 156210009;
    private const int PreviewReverseWeapon = 156211038;
    private const int PreviewUniqueRing = 330620433;
    private const int PreviewKrowazBoots = 208005000;
    private const int PreviewSealStone = 810890000;

    internal Node3D? SelfVisual => _self;

    internal Texture2D? TouchSlotIconUiPreview(int slotInPage)
    {
        SkillData.EnsureLoaded();
        if (_previewTouchSlots == null)
        {
            _previewTouchSlots = new int[TouchControls.ActionSlots];
            int filled = 0;
            foreach (var s in SkillData.ForClass(_selfClass != 0 ? _selfClass : 205))
            {
                if (s.Tree % 10 != 0 || s.Level < 1 || SkillData.Icon(s.Id) == null) continue;
                _previewTouchSlots[filled] = s.Id;
                if (++filled >= _previewTouchSlots.Length) break;
            }
        }
        int id = _previewTouchSlots[slotInPage % _previewTouchSlots.Length];
        return id == 0 ? null : SkillData.Icon(id);
    }

    private int[]? _previewTouchSlots;

    internal Control BuildExpBarUiPreview()
    {
        Sheet.ApplyLevel(61, 0, 168, 1000);
        ExpBarInit();
        UpdateExpBar();
        SetExpBarClock("14:33   Clear");
        if (_expBarStats != null) _expBarStats.Text = "60 fps  38 ms";
        return DetachPreviewControl(_expBarRoot!);
    }

    internal (Control Hp, Control Mp) BuildPotionButtonsUiPreview()
    {
        ItemData.EnsureLoaded();
        SkillData.EnsureLoaded();
        Inv.EnsureLength(GridStart + GridCount);
        Inv[GridStart + 1] = PreviewItem(PreviewHpPotion, PreviewPotionCount, 1);
        Inv[GridStart + 2] = PreviewItem(PreviewMpPotion, PreviewPotionCount, 1);
        return CreatePotionButtons();
    }

    internal string? HairStemFor(int race, int style) => HairPartStem(race, style);

    internal Control BuildStatusUiPreview()
    {
        _zone = 21;
        _myKoX = 267f;
        _myKoZ = 303f;
        BuildStatusHud();
        if (Platform.TouchUi) ScaleVitals(_statusRoot);
        _orb.Set(61, 23.269);
        _hpBar.Set(257, 257);
        _mpBar.Set(157, 157);
        UpdateStatusHud();
        return DetachPreviewControl((Control)_orb.GetParent());
    }

    internal Control BuildTargetUiPreview()
    {
        BuildTargetHud();
        _targetName.Text = "Worm   Lv 2";
        _targetHp.Set(32, 48);
        _targetBox.Visible = true;
        return DetachPreviewControl(_targetBox);
    }

    internal Control BuildZoneAbilityUiPreview(byte zoneType)
    {
        BuildZoneAbilityChip();
        ApplyZoneAbility(new ZoneAbilityInfo { ZoneType = zoneType, Tariff = 10 }, announce: false);
        _zoneabilityChip.Visible = true;
        return DetachPreviewControl(_zoneabilityChip);
    }

    internal Control BuildChatUiPreview()
    {
        Chat = new ChatSystem(this);
        Chat.Build();
        Chat.DetachNetwork();
        Chat.Append("[color=#e8e8e8]Zeus:[/color] Anyone hunting worms?");
        Chat.Append("[color=#5fd95f][Party][/color] Rin: Ready when you are.");
        Chat.Append("[color=#ff9a3c][Shout][/color] Moradon market is open!");
        for (int i = 0; i < 13; i++)
            Chat.Append($"[color=#bfc2c6]Nearby adventurer {i + 1} entered Moradon.[/color]");
        return DetachPreviewControl(Chat.Panel);
    }

    internal void AttachChatUiPreviewLayout(Control chat) =>
        Chat.AttachLayout(chat, persist: false);

    internal void SetChatUiPreviewState(bool inputActive, byte channel) =>
        Chat.SetPreviewState(inputActive, channel);

    internal bool FloaterUiPreview()
    {
        if (!_worldReady || Floaters == null) return false;
        PreviewFloaters();
        return true;
    }

    internal Control BuildCombatLogUiPreview()
    {
        BuildCombatLog();
        _combatLogRoot.Visible = true;
        for (int i = 0; i < 6; i++)
            CombatLogAdd($"You recovered {i + 2} MP.", CombatLogKind.Resource);
        CombatLogAdd("You recovered 42 HP.", CombatLogKind.Recovery);
        CombatLogAdd("You hit Worm for 14 damage.", CombatLogKind.Damage);
        CombatLogAdd("Worm hit you for 3 damage.", CombatLogKind.Incoming);
        CombatLogAdd("You hit Worm for 21 damage.", CombatLogKind.Damage);
        CombatLogAdd("You defeated Worm.", CombatLogKind.Outgoing);
        return DetachPreviewControl(_combatLogRoot);
    }

    internal void AttachCombatLogUiPreviewLayout(Control combatLog, Vector2 position) =>
        AttachCombatLogLayout(combatLog, () => position, persist: false);

    internal Control BuildHotbarUiPreview()
    {
        SkillData.EnsureLoaded();
        ItemData.EnsureLoaded();
        Inv.EnsureLength(GridStart + GridCount);
        Inv[GridStart] = PreviewItem(PreviewPotion, 12, 1);
        BuildHotbar();
        int filled = 0;
        foreach (var s in SkillData.ForClass(_selfClass != 0 ? _selfClass : 205))
        {
            if (s.Tree % 10 != 0 || s.Level < 1) continue;
            _hotbar[filled] = s.Id;
            if (++filled >= HotSlotsPerPage - 1) break;
        }
        _hotbar[HotSlotsPerPage - 1] = PreviewPotion;
        _hotSelected = 2;
        RefreshHotbar();

        for (int i = 0; i < _hotCells.Count; i++)
        {
            _hotCells[i].SetCooldown(i switch { 1 => 0.85f, 2 => 0.62f, 3 => 0.40f, 4 => 0.15f, _ => 0f });
            _hotCells[i].SetDim(i == 6);
            if (i is 5 or 6) _hotCells[i].SetCount(i == 5 ? 248 : 0, i == 5);
        }
        _hotCells[HotSlotsPerPage - 1].SetCount(12, true);
        return DetachPreviewControl(_hotbarBox);
    }

    internal void BuildInventoryTooltipHostUiPreview() => BuildInventoryPanel();

    internal Control? HoverHotbarSlotUiPreview(int slotInPage)
    {
        if (slotInPage < 0 || slotInPage >= _hotCells.Count) return null;
        _hotCells[slotInPage].EmitSignal(Control.SignalName.MouseEntered);
        return _itemTipPanel.Visible ? DetachPreviewControl(_itemTipPanel) : null;
    }

    internal Control BuildHudLauncherUiPreview() =>
        DetachPreviewControl(BuildHudLauncher());

    internal Control? BuildTownButtonUiPreview() =>
        _townButton == null ? null : DetachPreviewControl(_townButton);

    internal Control BuildBuffsUiPreview()
    {
        SkillData.EnsureLoaded();
        _myId = 1001;
        BuildBuffBar();

        int shortCap = Platform.TouchUi ? 2 : 4;
        int longCap = Platform.TouchUi ? 1 : 5;
        int shortLived = 0, longLived = 0;
        foreach (var s in SkillData.All)
        {
            if (s.Duration <= 0 || SkillData.Icon(s.Id) == null) continue;
            if (s.Type1 is not (MagicType.Buff or MagicType.DotHeal or MagicType.Stealth)) continue;
            bool isLong = s.Duration >= BuffLongSeconds;
            if (isLong ? longLived >= longCap : shortLived >= shortCap) continue;
            if (isLong) longLived++; else shortLived++;
            RegisterBuff(s, _myId, s.Duration);
            if (shortLived >= shortCap && longLived >= longCap) break;
        }
        BuffBarTick(Now());
        return DetachPreviewControl(_buffPanel!);
    }

    internal static Vector2 BuffsUiPreviewPosition(Vector2 viewport) =>
        viewport * 0.5f + BuffPanelFromCenter;

    internal Control BuildPremiumUiPreview()
    {
        BuildPremiumChip();
        OnPremiumStatus(1, 1, 52);
        return DetachPreviewControl(_premiumChip);
    }

    internal Control BuildAttendanceGiftUiPreview()
    {
        BuildAttendanceGift();
        SetAttendanceGift(2);
        return DetachPreviewControl(_attendanceGift);
    }

    internal Control BuildClockUiPreview()
    {
        BuildClockHud();
        UpdateClock(0.42f, Weather.Sunny);
        return DetachPreviewControl(_clockLabel);
    }

    internal Control BuildPrimaryWindowUiPreview(string windowName, int classCode = 201)
    {
        ItemData.EnsureLoaded();
        SkillData.EnsureLoaded();
        _myId = 1001;
        Vitals.Seed(hp: 214, maxHp: 257, mp: 151, maxMp: 157);
        _selfClass = classCode;
        Sheet.SeedStats(str: 60, sta: 72, dex: 184, intel: 50, mag: 50, points: 3);
        Sheet.SeedCombat(ap: 412, ac: 286);
        Sheet.ApplyDerived(new DerivedStats
        {
            TotalHit = 412, TotalAc = 286, MaxWeight = Sheet.MaxWeight,
            StrBonus = 57, StaBonus = 13, DexBonus = 10, IntBonus = 0, ChaBonus = -4,
            FireR = 20, ColdR = 15, LightningR = 15, MagicR = 10, DiseaseR = 8, PoisonR = 12,
        });
        Sheet.SeedWealth(gold: 1_180_000, np: 2_450);
        Sheet.SeedProgress(level: 82, exp: 18_704_073_876, maxExp: 24_523_906_932);
        Sheet.SeedResists(fire: 20, cold: 15, lightning: 15, magic: 10, disease: 8, poison: 12);
        Mastery.Seed(new byte[] { 4, 0, 0, 0, 0, 61, 12, 0, 0 });

        Sheet.SetMaxWeight(10_000);

        Inv.EnsureLength(InventoryConstants.InventoryTotal);
        Inv[6] = PreviewItem(PreviewUpgradedWeapon, 1, 7000);
        int[] previewBag =
        {
            389310000, 379001000, 379022000, 810684000, 811182000,
            900402000, 379040000, 320410011, 330310000, 810418000,
        };
        for (int i = 0; i < previewBag.Length; i++)
            Inv[GridStart + i] = PreviewItem(previewBag[i], (short)(3 + i * 7), 1);
        Inv[GridStart + 12] = PreviewItem(PreviewReverseWeapon, 1, 7000);
        Inv[GridStart + 13] = PreviewItem(201001000, 1, 3000);
        Inv[GridStart + 14] = PreviewItem(PreviewUniqueRing, 1, 1);

        Inv[InventoryConstants.CosWing] = PreviewItem(810178000, 1, 1);
        Inv[InventoryConstants.BagSlotFor(0)] = PreviewItem(700011000, 1, 1);
        Inv[InventoryConstants.MagicBagPageStart(0)] = PreviewItem(379001000, 40, 1);
        Inv[InventoryConstants.MagicBagPageStart(0) + 3] = PreviewItem(810418000, 1, 12);

        BuildStatsPanel();
        BuildFriendsPanel();
        RefreshStatsUI();
        _stHeaderName.Text = "Zeus";
        _stHeaderSub.Text = "Rogue";
        BuildInventoryPanel();
        RefreshInventoryUI();
        BuildSkillWindow();
        SeedPreviewQuests();
        BuildQuestLog();
        RefreshQuestLog();
        BuildPartyPanel();
        Net.I.SeedPartyPreview(
            new PartyMember(1001, 1, "Zeus", 257, 214, 61, 202, 157, 151),
            new PartyMember(1002, 1, "Rin", 742, 612, 80, 106, 122, 94),
            new PartyMember(1003, 1, "Aria", 304, 267, 60, 209, 688, 541));
        RefreshPartyUI();
        MainPanelInit();

        if (windowName == "Skills")
            RebuildSkillWindow();

        if (windowName == "Friends")
        {
            windowName = "Character";
            OnFriendList(new System.Collections.Generic.List<Network.FriendEntry>
            {
                new() { Name = "Hera", CharId = 1002, Status = 1, Level = 72, Class = 202, Nation = Nations.ElMorad, ZoneId = 21 },
                new() { Name = "Ares", CharId = 1003, Status = 3, Level = 80, Class = 201, Nation = Nations.ElMorad, ZoneId = 48 },
                new() { Name = "Athena", CharId = 1004, Status = 0, Level = 64, Class = 204, Nation = Nations.ElMorad },
            });
            ShowCharacterPage(CharacterPage.Friends);
        }

        if (!_mainWindows.TryGetValue(windowName, out HudWindow? window))
            window = _mainWindows["Inventory"];
        window.Visible = true;
        RefreshMainWindow(windowName);
        _stHeaderName.Text = "Zeus    Lv 61";
        _stHeaderSub.Text = "Rogue   ·   El Morad";
        return DetachPreviewControl(window);
    }

    internal void SelectAdminItemTabUiPreview(string tab) => _admItemSearch.SelectTab(tab);

    internal void SetAdminZoneFilterUiPreview(string filter)
    {
        _admZoneFilter.Text = filter;
        RefreshAdminZoneList();
    }

    internal void SelectSkillTabUiPreview(int tab) => SelectSkillTab(tab);

    internal void ToggleBagUiPreview(int bagIndex) => ToggleBag(InventoryConstants.BagSlotFor(bagIndex));

    internal Control BuildInventoryDeleteUiPreview()
    {
        AskDeleteItem(GridStart + 1);
        return DetachPreviewControl(_invDelPanel);
    }

    internal Control BuildAdminPanelUiPreview(string tab, string itemQuery = "raptor")
    {
        ItemData.EnsureLoaded();
        SkillData.EnsureLoaded();
        _isGm = true;
        _admEnabled = true;
        _selfClass = 105;
        Sheet.SeedProgress(level: 72, exp: 0, maxExp: 1_000_000);
        _admState = new AdminState
        {
            Granted = true,
            Class = 105, Level = 72,
            Str = 190, Sta = 148, Dex = 90, Intel = 60, MagicStat = 60,
            StatPoints = 12,
            MaxHp = 3_284, MaxMp = 812, Ap = 517, Ac = 936,
            Gold = 1_180_000,
            SkillPoints = new byte[] { 6, 0, 0, 0, 0, 63, 42, 21, 0 },
            ClassOptions = new[] { 101, 106 },
        };
        _zone = 21;
        BuildAdminPanel();
        SelectAdminTab(tab);
        if (tab == "Races")
        {
            OnAdminCollectionRaces(
            [
                new AdminCollectionRace { Id = 1, Name = "Moradon Rookie Roundup", ZoneId = 21, MinLevel = 1, MaxLevel = 35, DurationMinutes = 60, AutoStart = true, Active = true, RemainingSeconds = 41 * 60 + 12, Completions = 3, Schedule = "Sun 10:00, Wed 15:00", Objectives = "15 x Kecoon, 10 x Bulcan, 10 x Werewolf" },
                new AdminCollectionRace { Id = 2, Name = "Moradon Apple Harvest", ZoneId = 21, MinLevel = 1, MaxLevel = 35, DurationMinutes = 60, AutoStart = true, Schedule = "Mon 11:00, Thu 16:00", Objectives = "15 x Apples of Moradon, 5 x Teeth of Bandicoot, 5 x Silk bundle" },
                new AdminCollectionRace { Id = 5, Name = "Wolves of Moradon", ZoneId = 21, MinLevel = 1, MaxLevel = 83, DurationMinutes = 60, AutoStart = true, Schedule = "Daily 12:00", Objectives = "15 x Werewolf, 10 x Dark Eyes, 10 x Dire Wolf" },
                new AdminCollectionRace { Id = 21, Name = "Ronark Apostles of Flame", ZoneId = 71, MinLevel = 61, MaxLevel = 70, DurationMinutes = 60, AutoStart = false, Schedule = "manual", Objectives = "15 x Apostle of Flame, 10 x Doom Soldier, 10 x Troll, 2 enemy players" },
            ]);
        }
        if (tab == "Items")
        {
            _admItemSearch.SetQuery(itemQuery);
            _admItemSearch.Run();
            if (itemQuery == "raptor") _admItemSearch.SelectPlus(8);
        }
        SetAdminStatus("Stats set — STR 190 STA 148 DEX 90 INT 60 MP 60, 12 free.", false);
        _admShown = true;
        _admPanel.Visible = true;
        return DetachPreviewControl(_admPanel);
    }

    internal void WalkToForSmoke(float koX, float koZ)
    {
        if (_self == null) return;
        _moveTarget = GroundPos(koX, koZ, _myKoY, _selfLift);
        _hasMoveTarget = true;
        ShowMoveIndicator(_moveTarget);
    }

    internal (float X, float Z) KoPositionForSmoke() => (_myKoX, _myKoZ);

    internal void CastSkillForSmoke(int skillId) => CastSkill(skillId);

    internal void ShowAttendanceFailureUiPreview() =>
        OnAttendanceFailed(Net.EventBoardAttendanceClaim, Net.AttendanceClaimInventoryFull);

    internal Control BuildAttendanceUiPreview(bool noReply = false)
    {
        ItemData.EnsureLoaded();
        BuildItemTooltip();
        BuildAttendancePanel();
        if (noReply)
        {
            _attendanceShown = true;
            _attendancePanel.Visible = true;
            return DetachPreviewControl(_attendancePanel);
        }

        int total = Net.AttendanceDailySlots + Net.AttendanceBonusSlots;
        var slots = new int[total];
        var states = new byte[total];
        const int reached = 16;
        for (int i = 0; i < Net.AttendanceDailySlots; i++)
        {
            slots[i] = i + 1;
            states[i] = slots[i] < reached
                ? (i % 5 == 3 ? Net.AttendanceStateExpired : Net.AttendanceStateClaimed)
                : slots[i] == reached ? Net.AttendanceStateClaimable : AttendanceStateLocked;
        }
        for (int i = 0; i < Net.AttendanceBonusSlots; i++)
        {
            slots[Net.AttendanceDailySlots + i] = Net.AttendanceBonusFirstSlot + i;
            states[Net.AttendanceDailySlots + i] =
                i == 0 ? Net.AttendanceStateClaimable : AttendanceStateLocked;
        }

        OnAttendanceBoard(slots, states);
        _attendanceShown = true;
        _attendancePanel.Visible = true;
        return DetachPreviewControl(_attendancePanel);
    }

    internal Control OpenAttendanceUiPreview()
    {
        ItemData.EnsureLoaded();
        BuildItemTooltip();
        BuildAttendanceGift();
        BuildAttendancePanel();
        ShowAttendancePanel();
        return DetachPreviewControl(_attendancePanel);
    }

    internal Control? HoverAttendanceSlotUiPreview(int index)
    {
        if (index < 0 || index >= _attendanceGrid.GetChildCount()) return null;
        var socket = _attendanceGrid.GetChild(index)
            .GetChild(0).GetChild(0) as Control;
        socket?.EmitSignal(Control.SignalName.MouseEntered);
        return _itemTipPanel.Visible ? DetachPreviewControl(_itemTipPanel) : null;
    }

    internal Control BuildUpgradeUiPreview()
    {
        ItemData.EnsureLoaded();
        Sheet.SeedWealth(gold: 1_180_000, np: 2_450);
        Inv.EnsureLength(GridStart + GridCount);
        Inv[GridStart] = PreviewItem(PreviewUpgradedWeapon, 1, 7000);
        Inv[GridStart + 1] = PreviewItem(UpgradeScrollHighBlessed, 3, 1);
        Inv[GridStart + 2] = PreviewItem(TrinaPiece, 5, 1);
        Inv[GridStart + 3] = PreviewItem(PreviewReverseWeapon, 1, 6200);
        Inv[GridStart + 4] = PreviewItem(PreviewUniqueRing, 1, 1);
        Inv[GridStart + 5] = PreviewItem(PreviewPotion, 24, 1);
        BuildUpgradePanel();
        _upgradeAnvilId = 1;
        _upgradeShown = true;
        _upgradePanel.Visible = true;
        StageAnvilUpgrade(PreviewUpgradedWeapon, UpgradeScrollHighBlessed, TrinaPiece);
        OnUpgradeResult(new UpgradeResult(2, UpgradeTypePreview, UpgradeResultSucceeded,
            new[] { new UpgradeSlotResult(PreviewUpgradedResult, 0) }));
        return DetachPreviewControl(_upgradePanel);
    }

    internal Control BuildWarehouseUiPreview()
    {
        ItemData.EnsureLoaded();
        Sheet.SeedWealth(gold: 1_180_000, np: 2_450);
        Inv.EnsureLength(GridStart + GridCount);
        Inv[GridStart] = PreviewItem(PreviewUpgradedWeapon, 1, 7000);
        Inv[GridStart + 1] = PreviewItem(PreviewPotion, 24, 1);
        Inv[GridStart + 2] = PreviewItem(UpgradeScrollHighBlessed, 3, 1);
        BuildInventoryPanel();
        BuildWarehousePanel();
        _whShown = true;
        _whPanel.Visible = true;
        _whMoney = 4_820_000;
        _warehouse[0] = PreviewItem(PreviewReverseWeapon, 1, 6200);
        _warehouse[1] = PreviewItem(PreviewUniqueRing, 1, 1);
        _warehouse[2] = PreviewItem(PreviewUpgradedResult, 1, 7000);
        _warehouse[3] = PreviewItem(TrinaPiece, 12, 1);
        RefreshWarehouse();
        return DetachPreviewControl(_whPanel);
    }

    internal Control? HoverWarehouseCellUiPreview(bool bag, int index)
    {
        var cells = bag ? _whBagCells : _whCells;
        if (index < 0 || index >= cells.Length) return null;
        cells[index].EmitSignal(Control.SignalName.MouseEntered);
        return _itemTipPanel.Visible ? DetachPreviewControl(_itemTipPanel) : null;
    }

    internal Control BuildItemTooltipUiPreview(int itemId)
    {
        ItemData.EnsureLoaded();
        _selfClass = 105;
        Sheet.SeedProgress(level: 72, exp: 0, maxExp: 1_000_000);
        Sheet.SeedStats(str: 60, sta: 72, dex: 184, intel: 50, mag: 50, points: 3);
        Inv.EnsureLength(GridStart + GridCount);
        BuildInventoryPanel();

        var def = ItemData.Get(itemId);
        var ext = ItemData.ExtFor(itemId);
        int durability = (def?.Duration ?? 0) + (ext?.DurationBonus ?? 0);
        ShowItemTooltip(-1, new ItemSlot
        {
            ItemId = itemId,
            Count = 1,
            Durability = (short)Mathf.Min(durability, short.MaxValue),
        });
        return DetachPreviewControl(_itemTipPanel);
    }

    internal Control BuildNpcServiceChoiceUiPreview()
    {
        QuestText.EnsureLoaded();
        BuildNpcDialog();
        _vendorNpcName = "[Sundries] Zarta";
        OnRepairOpen(255000);
        return DetachPreviewControl(_npcPanel);
    }

    internal Control BuildAchievementUiPreview(AchievementTab category, bool hideClaimed = false)
    {
        AchievementInit();
        _achTab = category;
        _achOnSummary = false;
        ApplyAchievementPageVisibility();
        _achHideClaimed.ButtonPressed = hideClaimed;
        OnAchievementList(PreviewAchievementList());
        _achPanel.Visible = true;
        _achShown = true;
        RebuildAchievementList(keepScroll: false);
        return DetachPreviewControl(_achPanel);
    }

    internal Control BuildAchievementSummaryUiPreview(bool withReport = true)
    {
        AchievementInit();
        _achOnSummary = true;
        ApplyAchievementPageVisibility();
        OnAchievementList(PreviewAchievementList());

        if (withReport)
        {
            OnAchievementSummary(new AchievementSummary
            {
                PlayMinutes = 107401,
                MonstersDefeated = 139847,
                PlayersDefeated = 687,
                Deaths = 765,
                Points = 1700,
                RecentlyAchieved = PreviewRecentAchievements(),
                AchievedPerTab = new int[Net.AchievementTabCount],
            });
        }

        _achPanel.Visible = true;
        _achShown = true;
        RefreshAchievementCounts();
        RebuildAchievementSummary();
        return DetachPreviewControl(_achPanel);
    }

    private static int[] PreviewRecentAchievements()
    {
        var recent = new int[Net.AchievementRecentSlots];
        int slot = 0;
        foreach (int id in AchievementData.Ids)
        {
            if (slot >= recent.Length) break;
            if (AchievementData.Get(id) is not { } info || info.Name.Length == 0) continue;
            recent[slot++] = id;
        }
        return recent;
    }

    private static System.Collections.Generic.List<AchievementEntry> PreviewAchievementList()
    {
        var list = new System.Collections.Generic.List<AchievementEntry>();
        int index = 0;
        foreach (int id in AchievementData.Ids)
        {
            int state = (index % 9) switch
            {
                0 => Net.AchievementStateAchieved,
                1 or 2 => Net.AchievementStateClaimed,
                _ => Net.AchievementStateInProgress,
            };
            int target = 10 + index % 40 * 5;
            int progress = state == Net.AchievementStateInProgress
                ? target * (index % 7) / 8
                : target;
            list.Add(new AchievementEntry
            {
                Id = id, State = state, Progress = progress, Target = target,
            });
            index++;
        }
        return list;
    }

    internal Control BuildCapeUiPreview(bool asChief = true, int select = -1)
    {
        var me = Net.I.LastEnter;
        me.Name = asChief ? "Zeus" : "Tester10";
        Net.I.SeedPreviewEnter(me);

        CapeInit();
        _capePanel.Visible = true;
        _capeShown = true;

        OnCapeMyClan(new MyClanInfo
        {
            InClan = true, ClanId = 1, Name = "Olympus", Flag = 3, Members = 5,
            Chief = "Zeus", Grade = 1, Points = 800_000, PointFund = 5_000_000,
        });
        BuildCapeCatalogue();
        if (select >= 0)
        {
            ShowCapePattern(Cape.TryGet(select, out var pick) ? pick.M : 0);
            SelectCape(select);
        }
        UpdateCapeGate();
        return DetachPreviewControl(_capePanel);
    }

    internal Control BuildClanUiPreview(bool inClan, bool asChief = true, ClanTab tab = ClanTab.Members)
    {
        ClanInit();
        _clanPanel.Visible = true;
        _clanShown = true;

        if (inClan)
        {
            var me = Net.I.LastEnter;
            me.Name = asChief ? "Zeus" : "Tester10";
            Net.I.SeedPreviewEnter(me);

            OnMyClanInfo(new MyClanInfo
            {
                InClan = true, ClanId = 1, Name = "Olympus", Flag = 3, Members = 5,
                Chief = "Zeus", Grade = 1, Points = 800_000, PointFund = 5_000_000,
                Notice = "Siege practice tonight. Bring repair scrolls.",
            });
            OnClanMembers(new System.Collections.Generic.List<ClanMember>
            {
                new() { Name = "Zeus", Fame = 1, Level = 61, Class = 207, IsOnline = true },
                new() { Name = "Hera", Fame = 2, Level = 72, Class = 202, IsOnline = true },
                new() { Name = "Ares", Fame = 3, Level = 72, Class = 201, IsOnline = false },
                new() { Name = "Athena", Fame = 5, Level = 72, Class = 204, IsOnline = true },
                new() { Name = "Tester10", Fame = 5, Level = 31, Class = 205, IsOnline = true },
            });

            if (tab == ClanTab.Donations)
            {
                ShowClanTab(ClanTab.Donations);
                OnClanDonationList(new System.Collections.Generic.List<(string, int)>
                {
                    ("Hera", 41_200), ("Zeus", 33_900), ("Ares", 12_050), ("Athena", 800),
                });
            }
        }
        else
        {
            OnMyClanInfo(new MyClanInfo { InClan = false });
            OnClanList(new System.Collections.Generic.List<ClanBrowseEntry>
            {
                new() { Id = 1, Name = "Olympus", Chief = "Zeus", Members = 5, Flag = 3, Points = 800_000 },
                new() { Id = 2, Name = "Asgard", Chief = "Odin", Members = 22, Flag = 2, Points = 361_400 },
                new() { Id = 3, Name = "Avalon", Chief = "Arthur", Members = 8, Flag = 1, Points = 12_000 },
            });
        }

        return DetachPreviewControl(_clanPanel);
    }

    internal Control BuildWarpUiPreview(bool blocked = false)
    {
        Sheet.SeedProgress(level: 42, exp: 0, maxExp: 1_000_000);
        Sheet.SeedWealth(9_500, 0);
        BuildWarpPanel();
        _vendorNpcName = "El Morad Warp Gate";
        OnWarpList(new System.Collections.Generic.List<Net.WarpListEntry>
        {
            new(2121, "Folk Village", "", 22, 0, 3_000),
            new(2122, "Tale Village", "", 22, 0, 3_000),
            new(2123, "El Morad Castle", "", 2, 0, 5_000),
            new(2124, "Lunar Valley", "", 2, 0, 10_000),
            new(2125, "Delos", "", 30, 0, 17_000),
            new(2126, "Ardream", "", 72, 0, 3_000),
            new(2127, "Ronark Land Base", "", 73, 0, 17_000),
            new(2128, "Ronark Land", "", 71, 0, 17_000),
        });
        int row = blocked ? 7 : 2;
        SelectWarpRow(row);
        Callable.From(() => SelectWarpRow(row)).CallDeferred();
        return DetachPreviewControl(_warpPanel);
    }

    internal Control BuildNpcMsgUiPreview(bool quests)
    {
        QuestText.EnsureLoaded();
        BuildNpcDialog();
        if (quests)
        {
            Sheet.SeedProgress(level: 60, exp: 0, maxExp: 1_000_000);
            _selfClass = 202;
            _zone = 21;
            _quests.Add(new QuestEntry(60, 1));
            _quests.Add(new QuestEntry(62, 2));
            _quests.Add(new QuestEntry(65, 3));
            _quests.Add(new QuestEntry(67, 4));
            OnNpcMsg(167, 13013);
        }
        else
        {
            OnNpcMsg(9170, 31511);
        }
        return DetachPreviewControl(_npcPanel);
    }

    internal void SetQuestViewUiPreview(QuestView view)
    {
        if (_npcPanel == null && !view.Notification)
        {
            _questSelected = view.QuestId;
            OnQuestView(view with { Open = false });
            return;
        }
        OnQuestView(view);
    }

    internal Control BuildQuestOfferUiPreview(string kind = "")
    {
        ItemData.EnsureLoaded();
        QuestText.EnsureLoaded();
        Inv.EnsureLength(GridStart + GridCount);
        Inv[GridStart] = PreviewItem(810090000, 1, 1);
        Sheet.SeedWealth(gold: 1_180_000, np: 2_450);
        _selfClass = 1;
        BuildItemTooltip();
        BuildNpcDialog();
        ShowQuestView(kind == "talk"
            ? new QuestView(663, 31561, 2, true, true, false, false,
                QuestViewState.Available, 0, "How to hunt",
                "You must talk to [Hunter] Halon.",
                "So you think you've got enough skills? Have you even had a proper hunting before?",
                new QuestObjectives(663, false, []), [], [], [])
            : kind == "fee"
            ? new QuestView(71, 18004, 21, true, true, false, false,
                QuestViewState.Available, 0, "1st job change",
                "I have to pay [Grand Merchant] Kaishan for the job change.",
                "So you want to change jobs? It costs 3,000 coins to register the change with the guild.",
                new QuestObjectives(71, false, []), [],
                [
                    new QuestTransfer(true, 1, 0, 3000, 0),
                    new QuestTransfer(false, 4, 0, 1, 0),
                    new QuestTransfer(false, 2, 0, 1500, 0),
                ], [])
            : new QuestView(273, 14201, 2, true, true, false, false,
                QuestViewState.Available, 0, "2nd job change",
                "I promised to bring [Warrior Master] Skaki collectible items.",
                "Oh, it's time for you to undergo a 2nd job change? You have persevered well. Don't be lax as "
                + "the true chivalric mission begins now. You need the ingredients below to perform the 2nd job "
                + "change ritual.",
                new QuestObjectives(273, false, []), [],
                [
                    new QuestTransfer(true, 0, 810095000, 1, 0),
                    new QuestTransfer(true, 0, 810090000, 1, 0),
                    new QuestTransfer(true, 0, 810094000, 1, 0),
                    new QuestTransfer(false, 5, 0, 1, 0),
                ], []));
        return DetachPreviewControl(_npcPanel);
    }

    internal Control? HoverQuestOfferRowUiPreview(int index)
    {
        var rows = _npcQuestContent.GetChildren()
            .OfType<PanelContainer>()
            .SelectMany(section => section.GetChildren().OfType<VBoxContainer>())
            .SelectMany(body => body.GetChildren().OfType<Control>())
            .ToList();
        if (index < 0 || index >= rows.Count) return null;
        rows[index].EmitSignal(Control.SignalName.MouseEntered);
        return _itemTipPanel.Visible ? DetachPreviewControl(_itemTipPanel) : null;
    }

    internal Control BuildQuestMapUiPreview()
    {
        _zone = 21;
        _miniMap = new MiniMap();
        AddChild(_miniMap);
        _miniMap.SetZone("moradon", "Moradon");
        OnQuestTarget(new QuestTargetDetail(62, 21, 632, 465, "Bandicoot hunt", "Bandicoot",
            "Bandicoot\nA rodent which grew abnormally by feeding on corpses of evil races in the War with Pathos.",
            "Inhabits Seashore of Hope"));
        return DetachPreviewControl(_questTargetWindow!);
    }

    internal Control BuildQuestReceiptUiPreview()
    {
        OnQuestReceipt(new QuestReceipt(62, [
            new QuestReceiptEntry(900001000, 375), new QuestReceiptEntry(900000000, 2700)]));
        return DetachPreviewControl(_questReceiptWindow!);
    }

    internal Control BuildQuestProgressToastUiPreview(string quest, string objective, int done, int needed)
    {
        ShowQuestProgressToast(quest, objective, done, needed);
        return DetachPreviewControl(_questToastPanel!);
    }

    internal Control ShowNoticeUiPreview(string text)
    {
        Chat.ShowNoticePreview(text);
        return DetachPreviewControl(Chat.NoticePanel);
    }

    private static List<MailEntry> MailUiPreviewEntries() =>
    [
        new MailEntry
        {
            Id = 3, Sender = "LibreKO", Subject = "Collection Race: Moradon Rookie Roundup", Read = false,
            Attachments = MailAttachmentState.Pending, SentAt = DateTime.UtcNow.AddMinutes(-12),
            Items =
            [
                new MailAttachment { Kind = MailAttachmentKind.Gold, ItemId = QuestData.CoinItemId, Count = 300_000 },
                new MailAttachment { Kind = MailAttachmentKind.Experience, ItemId = QuestData.ExpItemId, Count = 150_000 },
                new MailAttachment { Kind = MailAttachmentKind.Item, ItemId = 379154000, Count = 1 },
            ],
        },
        new MailEntry
        {
            Id = 2, Sender = "Rikka", Subject = "Apples for the raid", Read = false,
            Attachments = MailAttachmentState.Pending, SentAt = DateTime.UtcNow.AddHours(-5),
            Items = [new MailAttachment { Kind = MailAttachmentKind.Item, ItemId = 810418000, Count = 20 }],
        },
        new MailEntry
        {
            Id = 1, Sender = "Zeus", Subject = "Welcome to the clan", Read = true,
            Attachments = MailAttachmentState.None, SentAt = DateTime.UtcNow.AddDays(-3),
        },
    ];

    internal (Control Inventory, CanvasLayer MailLayer) BuildMailDragUiPreview()
    {
        var inventory = BuildPrimaryWindowUiPreview("Inventory");
        _mailDragSeedSlot = Inv.FirstFreeGridSlot();
        if (_mailDragSeedSlot >= 0)
        {
            Inv[_mailDragSeedSlot] = new ItemSlot { ItemId = 389018000, Count = 5, Durability = 1 };
            Inv[_mailDragSeedSlot + 1] = new ItemSlot { ItemId = 379154000, Count = 1, Durability = 1 };
            RefreshInventoryUI();
        }
        BuildMailWindow();
        BuildMailComposeWindow();
        _mailContacts.UnionWith(["Rikka"]);
        _mailComposeShown = true;
        _mailComposeWindow.Visible = true;
        _mailTo.Text = "Rikka";
        _mailSubject.Text = "Drag test";
        OnMailBodyChanged();
        RenderMailAttachments();
        RemoveChild(_mailLayer);
        return (inventory, _mailLayer);
    }

    private int _mailDragSeedSlot = -1;

    internal (Vector2 From, Vector2 To)? MailDragPointsUiPreview(int offset)
    {
        var abs = _mailDragSeedSlot + offset;
        var cell = _mailDragSeedSlot < 0 ? null : _invBagCells.FirstOrDefault(c => c.Slot == abs);
        if (cell == null || !cell.IsInsideTree())
            return null;
        return (cell.GetGlobalRect().GetCenter(), _mailDropZone.GetGlobalRect().GetCenter());
    }

    internal string MailDragDebugUiPreview()
    {
        var has = _invCells.TryGetValue(_mailDragSeedSlot, out var cell);
        return $"seed={_mailDragSeedSlot} len={Inv.Length} cells={_invCells.Count} has={has} inTree={(has && cell!.IsInsideTree())} keys={string.Join(',', _invCells.Keys.OrderBy(k => k))}";
    }

    internal string MailComposeSizeUiPreview() =>
        $"window size={_mailComposeWindow.Size} min={_mailComposeWindow.GetCombinedMinimumSize()} attachments={_mailAttachments.Count} body={_mailComposeWindow.Body.Size}";

    internal Control BuildMailUiPreview()
    {
        ItemData.EnsureLoaded();
        BuildMailWindow();
        BuildMailComposeWindow();
        _mailShown = true;
        _mailWindow.Visible = true;
        OnMailUnread(2);
        OnMailList(MailUiPreviewEntries());
        return DetachPreviewControl(_mailWindow);
    }

    internal Control BuildMailReadUiPreview()
    {
        ItemData.EnsureLoaded();
        BuildMailWindow();
        BuildMailComposeWindow();
        OnMailList(MailUiPreviewEntries());
        SelectMail(3);
        OnMailRead(3, true, "You completed the Collection Race 'Moradon Rookie Roundup' in Moradon. Your rewards are attached to this mail.");
        return DetachPreviewControl(_mailReadWindow);
    }

    internal Control BuildMailComposeUiPreview(bool lateAttach = false)
    {
        var window = BuildMailComposeUiPreviewNow();
        if (!lateAttach) return window;
        _mailAttachments.RemoveAt(1);
        RenderMailAttachments();
        var later = new Godot.Timer { WaitTime = 1.0, OneShot = true, Autostart = true };
        later.Timeout += () =>
        {
            var data = new Godot.Collections.Dictionary { { "id", Inv[Inventory.GridStart + 1].ItemId }, { "invFrom", Inventory.GridStart + 1 } };
            _mailDropZone._CanDropData(Vector2.Zero, data);
            _mailDropZone._DropData(Vector2.Zero, data);
            BuildInventoryTooltipHostUiPreview();
            ShowItemTooltip(Inventory.GridStart + 1, Inv[Inventory.GridStart + 1]);
        };
        window.AddChild(later);
        return window;
    }

    private Control BuildMailComposeUiPreviewNow()
    {
        ItemData.EnsureLoaded();
        BuildMailWindow();
        BuildMailComposeWindow();
        Inv.EnsureLength(InventoryConstants.InventoryTotal);
        Inv[Inventory.GridStart] = new ItemSlot { ItemId = 810418000, Count = 20, Durability = 1 };
        Inv[Inventory.GridStart + 1] = new ItemSlot { ItemId = 379154000, Count = 1, Durability = 1 };
        Inv[Inventory.GridStart + 2] = new ItemSlot { ItemId = 389018000, Count = 5, Durability = 1 };
        _mailContacts.UnionWith(["Rikka", "Zeus", "Ariel", "Marduk"]);
        _mailComposeShown = true;
        _mailComposeWindow.Visible = true;
        _mailTo.Text = "Rikka";
        _mailSubject.Text = "Apples for the raid";
        _mailBody.Text = "Here are the apples you asked for. Good hunting!";
        _mailGold.Value = 25_000;
        _mailAttachments.Add((Inventory.GridStart, 20));
        _mailAttachments.Add((Inventory.GridStart + 1, 1));
        OnMailBodyChanged();
        RenderMailAttachments();
        return DetachPreviewControl(_mailComposeWindow);
    }

    internal Control BuildCollectionRaceUiPreview(bool completed)
    {
        ItemData.EnsureLoaded();
        BuildCollectionRaceWindow();
        ShowCollectionRace(new CollectionRaceState
        {
            Name = "Moradon Rookie Roundup",
            RemainingSeconds = 56 * 60,
            Objectives =
            [
                new CollectionRaceObjective { Kind = CollectionRaceObjectiveKind.Monster, TargetId = 150, Name = "Kecoon", Count = 15, Current = completed ? 15 : 4 },
                new CollectionRaceObjective { Kind = CollectionRaceObjectiveKind.Monster, TargetId = 250, Name = "Bulcan", Count = 10, Current = 10 },
                new CollectionRaceObjective { Kind = CollectionRaceObjectiveKind.Item, TargetId = 810418000, Name = "Apples of Moradon", Count = 15, Current = completed ? 15 : 7 },
            ],
            IsCompleted = completed,
            Rewards =
            [
                new CollectionRaceReward { ItemId = QuestData.CoinItemId, ItemCount = 500_000, Rate = 100 },
                new CollectionRaceReward { ItemId = QuestData.ExpItemId, ItemCount = 250_000, Rate = 100 },
                new CollectionRaceReward { ItemId = QuestData.LadderPointItemId, ItemCount = 100, Rate = 100 },
                new CollectionRaceReward { ItemId = 379154000, ItemCount = 1, Rate = 35 },
            ],
        });
        return DetachPreviewControl(_crWindow);
    }

    internal Control BuildQuestNotificationUiPreview(int count = 3)
    {
        QuestText.EnsureLoaded();
        var samples = new[]
        {
            new QuestView(663, 31561, 2, true, true, false, false, QuestViewState.Available, 0, "How to hunt",
                "You must talk to [Hunter] Halon.",
                "So you think you've got enough skills? Have you even had a proper hunting before? Come and see me at the camp.",
                new QuestObjectives(663, false, []), [], [], ["I'll come by", "Not now"], Notification: true),
            new QuestView(60, 13013, 21, true, true, false, false, QuestViewState.Available, 0, "Doom Soldier hunt",
                "Hunt 40 Doom Soldiers for the guard captain.",
                "The Doom Soldiers are pressing on the north gate again. Thin their ranks and the captain will reward you.",
                new QuestObjectives(60, false, []), [], [], ["Accept"], Notification: true),
            new QuestView(1233, 25002, 21, true, false, true, false, QuestViewState.Claimable, 0, "Draki's Heart",
                "Bring the Spiritual Stone to the sage.",
                "You found it. Bring the stone to me and I will tell you what the Draki left behind.",
                new QuestObjectives(1233, false, []), [], [], ["Turn in"], Notification: true),
        };
        for (var i = 0; i < Mathf.Min(count, samples.Length); i++)
            ShowQuestNotification(samples[i]);
        return DetachPreviewControl(_questNotificationWindow!);
    }

    internal void SetNpcDialogUiPreview(NpcDialog dialog, int scroll)
    {
        OnNpcDialog(dialog);
        _npcMenuScroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, scroll);
    }

    internal Control BuildNpcDialogUiPreview(bool full, bool rewards = false)
    {
        QuestText.EnsureLoaded();
        BuildNpcDialog();
        var dlg = new NpcDialog
        {
            NpcId = 506,
            Flag = 1,
            HeaderTextId = full ? 9309 : 200,
            ScriptFile = "moradon_guide.quest",
        };
        int[] menuIds = full
            ? new[] { 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 10 }
            : new[] { 11, 12, 13, 10 };
        for (int i = 0; i < menuIds.Length; i++) dlg.Buttons.Add((i, menuIds[i], null));
        if (rewards)
        {
            dlg.HeaderText = "Choose your reward.";
            dlg.Buttons.Clear();
            dlg.Buttons.Add((0, 0, "1,500,000 experience"));
            dlg.Buttons.Add((1, 0, "100,000 coins"));
            dlg.Buttons.Add((2, 0, "Cancel"));
        }
        OnNpcDialog(dlg);
        return DetachPreviewControl(_npcPanel);
    }

    internal Control BuildVendorUiPreview()
    {
        ItemData.EnsureLoaded();
        Sheet.SeedWealth(gold: 1_180_000, np: 2_450);
        Inv.EnsureLength(GridStart + GridCount);
        Inv[GridStart] = PreviewItem(811182000, 35, 1);
        Inv[GridStart + 1] = PreviewItem(810684000, 12, 1);
        Inv[GridStart + 2] = PreviewItem(900402000, 5, 1);
        BuildInventoryPanel();
        BuildVendorPanel();
        _vendorNpcName = "[Potion Merchant] Euronia";
        OnVendorOpen(255000);
        return DetachPreviewControl(_vendorPanel);
    }

    internal Control? HoverVendorRowUiPreview(bool sell, int index)
    {
        var rows = (sell ? _vendorSellList : _vendorBuyList).GetChildren();
        if (index < 0 || index >= rows.Count || rows[index] is not Control row) return null;
        row.EmitSignal(Control.SignalName.MouseEntered);
        return _itemTipPanel.Visible ? DetachPreviewControl(_itemTipPanel) : null;
    }

    internal Control BuildBuyAmountUiPreview()
    {
        ItemData.EnsureLoaded();
        Sheet.SeedWealth(gold: 1_180_000, np: 2_450);
        Sheet.SetMaxWeight(4_500);
        Inv.EnsureLength(GridStart + GridCount);
        Inv[GridStart] = PreviewItem(811182000, 35, 1);
        BuildBuyAmountPrompt();
        var entry = new ItemData.SellEntry { Id = 389013000, Line = 0, List = 0 };
        OpenBuyAmount(entry, ItemData.Get(entry.Id)!);
        SetBuyAmount(25);
        var box = DetachPreviewControl(_buyAmountBox);
        Callable.From(() => box.Size = box.GetCombinedMinimumSize()).CallDeferred();
        return box;
    }

    internal Control BuildClassChangeUiPreview()
    {
        BuildClassChangePanel();
        _vendorNpcName = "Captain Kaishan";
        OnClassChangeNpc();
        return DetachPreviewControl(_classChangePanel);
    }

    internal Control BuildQuestTrackerUiPreview()
    {
        SeedPreviewQuests();
        BuildQuestTracker();
        RefreshTracker();
        return DetachPreviewControl(_trackerPanel);
    }

    internal Control BuildDeathDialogUiPreview(bool respawnPending)
    {
        BuildDeathDialog();
        _selfDead = true;
        _deathExpLost = 18_240;
        ShowDeathDialog();
        if (respawnPending) MarkRespawnPending();
        return DetachPreviewControl(_deathPanel);
    }

    internal Control BuildUserInfoUiPreview()
    {
        UserInfoInit();
        RequestUserInformation("Tester10");
        OnUserInformation(true, new Net.UserInformation(
            "Tester10", 61, 202, 2_450, 380,
            17, 3, 1, 1, "Noah Knights", "Ardream", 2, 1,
            [208003000, 208001000, 208002000, 208004000, 208005000, 136710000, 127410000]));
        return DetachPreviewControl(_userInfoPanel);
    }

    internal Control BuildEquipViewUiPreview()
    {
        ItemData.EnsureLoaded();
        BuildInventoryPanel();
        EquipViewInit();
        RequestEquipmentView("Tester10");
        OnEquipmentView(Net.EquipmentViewResult.Accepted, new Net.EquipmentView(
            "Tester10", 202, 12, 1, 3, 61, 1, Nations.ElMorad,
            257, 157,
            60, 5, 72, 0, 184, 12, 50, 0, 50, 0,
            412, 286, 20, 15, 15, 10, 8, 12,
            new System.Collections.Generic.List<(int, int, short, byte)>
            {
                (InventoryConstants.Head, 208003000, 7000, 0),
                (InventoryConstants.Breast, 208001000, 6400, 0),
                (InventoryConstants.Glove, 208004000, 5100, 0),
                (InventoryConstants.Leg, 208002000, 6100, 0),
                (InventoryConstants.Foot, 208005000, 4800, 0),
                (InventoryConstants.RightHand, 127410000, 5600, 0),
                (InventoryConstants.LeftHand, 136710000, 5200, 0),
                (InventoryConstants.Neck, 330310000, 4200, 0),
                (InventoryConstants.RightRing, 320410011, 3900, 0),
            }));
        return DetachPreviewControl(_equipViewPanel);
    }

    internal Control BuildWhisperUiPreview(bool minimized = false)
    {
        Chat = new ChatSystem(this);
        WhisperInit();
        OpenWhisperWith("Tester10");
        var chat = _whispers["Tester10"];
        if (Net.I.WhisperHistory("Tester10").Count == 0)
        {
            AppendWhisper(chat, mine: false, notice: false, "hey, are you farming Ronark?");
            AppendWhisper(chat, mine: true, notice: false, "yep, west side by the bridge. bring pots");
            AppendWhisper(chat, mine: false, notice: false, "omw");
            AppendWhisper(chat, mine: true, notice: false, "invite me when you get here");
            AppendWhisper(chat, mine: false, notice: false, "which channel are you on?");
            AppendWhisper(chat, mine: true, notice: false, "channel 2, near the north gate");
            AppendWhisper(chat, mine: false, notice: false, "ok give me a sec, repairing");
            AppendWhisper(chat, mine: true, notice: false, "no rush, I'll hold the spot");
            AppendWhisper(chat, mine: false, notice: false, "ready, invite me");
        }
        if (!minimized) return DetachPreviewControl(chat.Window);

        chat.Window.SetMinimized(true);
        var longName = GetOrCreateWhisper("Bartholomew the Third", minimized: true);
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 8);
        stack.AddChild(DetachPreviewControl(chat.Window));
        stack.AddChild(DetachPreviewControl(longName.Window));
        return stack;
    }

    internal Control BuildTradeWaitUiPreview()
    {
        BuildExchangeWaitPanel();
        ShowExchangeWait("Waiting for Tester10 to accept the trade…");
        return DetachPreviewControl(_exWaitLabel.GetParent().GetParent().GetParent<Control>());
    }

    internal Control BuildTradeAmountUiPreview()
    {
        ItemData.EnsureLoaded();
        Inv.EnsureLength(GridStart + GridCount);
        Inv[GridStart] = PreviewItem(389310000, 137, 1);
        BuildExchangeAmountPrompt();
        OpenExchangeAmount(GridStart, 137);
        return DetachPreviewControl(_exAmountIcon.GetParent().GetParent().GetParent().GetParent<Control>());
    }

    internal Control BuildSealUiPreview(
        SealMode mode, bool socketed, ItemFlag state = ItemFlag.Unsealed, bool pad = false)
    {
        ItemData.EnsureLoaded();
        Inv.EnsureLength(GridStart + GridCount);
        var boots = PreviewItem(PreviewKrowazBoots, 1, 100);
        boots.Flag = (byte)state;
        Inv[GridStart] = boots;
        Inv[GridStart + 1] = PreviewItem(PreviewSealStone, 4, 0);
        Inv[GridStart + 2] = PreviewItem(PreviewHpPotion, PreviewPotionCount, 1);
        Inv[GridStart + 3] = PreviewItem(PreviewUniqueRing, 1, 100);
        Sheet.SetGold(151_235_840);
        BuildSealWindow();
        OpenSealWindow(mode);
        if (socketed) TakeSealSocket(0);
        if (pad) ShowSealKeypadUiPreview();
        return DetachPreviewControl(_sealPanel);
    }

    internal void ShowSealKeypadUiPreview()
    {
        _sealPad.Visible = true;
        for (int i = 0; i < 5; i++) PushSealDigit(i + 1);
    }

    internal void SetQuestObjectivesUiPreview(QuestObjectives objectives)
    {
        OnQuestObjectives(objectives);
        SelectQuest(objectives.QuestId);
    }

    private void SeedPreviewQuests()
    {
        if (_quests.Count != 0) return;
        _quests.Add(new QuestEntry(60, 1));
        _quests.Add(new QuestEntry(62, 1));
        _quests.Add(new QuestEntry(274, 1));
        _quests.Add(new QuestEntry(275, 3));
        _quests.Add(new QuestEntry(278, 1));
        _quests.Add(new QuestEntry(808, 2));
        _quests.Add(new QuestEntry(810, 2));
        _quests.Add(new QuestEntry(1091, 4));
        _questKills[60] = new ushort[] { 3, 0, 0, 0 };
        _questKills[62] = new ushort[] { 1, 0, 0, 0 };
        _questKills[274] = new ushort[] { 13, 0, 0, 0 };
        _questKills[275] = new ushort[] { 20, 0, 0, 0 };
        _questTracked.Add(274);
        _questSelected = 60;
    }

    internal Control BuildMerchantMenuUiPreview()
    {
        ItemData.EnsureLoaded();
        MerchantInit();
        _merchantMenu.Visible = true;
        _merchantMenuShown = true;
        return DetachPreviewControl(_merchantMenu);
    }

    internal Control BuildSellStallUiPreview(bool staged)
    {
        SeedMerchantPreview();
        MerchantInit();
        OnMerchantOpenResult(Net.MerchantOpenAccepted);
        if (staged)
        {
            OnMerchantItemAdd(true, 156211000, 1, 30, 350_000_000, 0, 0);
            OnMerchantItemAdd(true, 379080000, 8, 0, 5_000_000, 2, 1);
            OnMerchantItemAdd(true, 811182000, 35, 1, 12_500, 3, 2);
            _sellAdvert.Text = "cheap gear, come look";
        }
        return DetachPreviewControl(_sellStallPanel);
    }

    internal Control? HoverStallCellUiPreview(int index)
    {
        if (index < 0 || index >= _sellStallCells.Length) return null;
        _sellStallCells[index].EmitSignal(Control.SignalName.MouseEntered);
        return _itemTipPanel.Visible ? DetachPreviewControl(_itemTipPanel) : null;
    }

    internal Control BuildStallSignUiPreview()
    {
        SeedMerchantPreview();
        MerchantInit();

        var stall = new Stall { IsBuying = false, ItemIds = new[] { 156211051, 156210008, 0, 330620433 } };
        _stalls[4242] = stall;
        RefreshStallSign(4242, stall);
        stall.Sign!.Visible = true;
        return DetachPreviewControl(stall.Sign);
    }

    internal Control? HoverStallSignUiPreview(int index)
    {
        if (!_stalls.TryGetValue(4242, out var stall) || index >= stall.SignCells.Length) return null;
        stall.SignCells[index].EmitSignal(Control.SignalName.MouseEntered);
        return _itemTipPanel.Visible ? DetachPreviewControl(_itemTipPanel) : null;
    }

    internal Control BuildShopUiPreview()
    {
        SeedMerchantPreview();
        MerchantInit();
        OnMerchantList(4242, new[]
        {
            PreviewStallItem(156211000, 1, 30, 350_000_000),
            PreviewStallItem(379080000, 8, 0, 5_000_000),
            PreviewStallItem(811182000, 35, 1, 12_500),
            default, default, default, default, default, default, default, default, default,
        });
        _merchantAdverts[4242] = "cheap gear, come look";
        _shopPanel.Title = ShopTitle(4242);
        return DetachPreviewControl(_shopPanel);
    }

    internal Control BuildTradeConfirmUiPreview()
    {
        SeedMerchantPreview();
        MerchantInit();
        OnMerchantList(4242, new[]
        {
            PreviewStallItem(379080000, 40, 0, 5_000_000),
            default, default, default, default, default,
            default, default, default, default, default, default,
        });
        BuyFromStall(0);
        return DetachPreviewControl((Control)_amountLayer.GetChild(1));
    }

    internal Control BuildPricePromptUiPreview(bool stackable)
    {
        SeedMerchantPreview();
        MerchantInit();
        OnMerchantOpenResult(Net.MerchantOpenAccepted);
        StageStallItem(stackable ? 2 : 0);
        TypePriceUiPreview("50000000");
        return DetachPreviewControl((Control)_amountLayer.GetChild(1));
    }

    internal string TypePriceUiPreview(string typed)
    {
        _amountPrice.GrabFocus();
        _amountPrice.Text = "";
        foreach (char c in typed)
        {
            _amountPrice.Text += c;
            _amountPrice.CaretColumn = _amountPrice.Text.Length;
            _amountPrice.EmitSignal(LineEdit.SignalName.TextChanged, _amountPrice.Text);
        }
        return $"shown=\"{_amountPrice.Text}\" value={_amountPrice.Value} total={_amountTotal.Text}";
    }

    internal Control BuildWishListUiPreview(bool filled)
    {
        SeedMerchantPreview();
        MerchantInit();
        OpenWishList();
        if (filled)
        {
            _wishes[0] = new Network.MerchantWishItem { ItemId = 156211000, Count = 1, Price = 350_000_000 };
            _wishes[1] = new Network.MerchantWishItem { ItemId = 379080000, Count = 20, Price = 4_800_000 };
            RefreshWishList();
        }
        return DetachPreviewControl(_wishPanel);
    }

    internal Control BuildWishQuantityUiPreview(string query)
    {
        SeedMerchantPreview();
        MerchantInit();
        OpenWishList();
        OnWishSlotClicked(0);
        _wishFind.SetQuery(query);
        _wishFind.Run();
        _wishFind.RegisterFirstUiPreview();
        return DetachPreviewControl((Control)_amountLayer.GetChild(1));
    }

    internal Control BuildItemSearchUiPreview(string query)
    {
        SeedMerchantPreview();
        MerchantInit();
        OpenWishList();
        OnWishSlotClicked(0);
        _wishFind.SetQuery(query);
        _wishFind.Run();
        return DetachPreviewControl(_wishFindPanel);
    }

    internal Control BuildWantedStallUiPreview()
    {
        SeedMerchantPreview();
        MerchantInit();
        OnBuyMerchantList(4242, new[]
        {
            PreviewStallItem(379080000, 20, 0, 4_800_000),
            PreviewStallItem(811182000, 50, 1, 9_000),
            default, default, default, default, default, default, default, default, default, default,
        });
        return DetachPreviewControl(_wantedPanel);
    }

    private void SeedMerchantPreview()
    {
        ItemData.EnsureLoaded();
        Sheet.SeedWealth(gold: 9_250_671, np: 2_450);
        Inv.EnsureLength(GridStart + GridCount);
        Inv[GridStart] = PreviewItem(156211000, 1, 30);
        Inv[GridStart + 1] = PreviewItem(810684000, 12, 1);
        Inv[GridStart + 2] = PreviewItem(379080000, 128, 0);
        Inv[GridStart + 3] = PreviewItem(811182000, 35, 1);
        Inv[GridStart + 5] = PreviewItem(900402000, 5, 1);
        BuildItemTooltip();
    }

    private static Network.MerchantStallItem PreviewStallItem(int id, int count, short durability, int price) => new()
    {
        ItemId = id,
        Count = count,
        Durability = durability,
        Price = price,
    };

    private static ItemSlot PreviewItem(int id, int count, int durability) => new()
    {
        ItemId = id,
        Count = (short)count,
        Durability = (short)durability,
    };

    private static Control DetachPreviewControl(Control control)
    {
        foreach (Node child in control.GetChildren())
        {
            if (child is not HudLayout layout) continue;
            control.RemoveChild(layout);
            layout.Free();
        }
        control.GetParent()?.RemoveChild(control);
        return control;
    }
}
