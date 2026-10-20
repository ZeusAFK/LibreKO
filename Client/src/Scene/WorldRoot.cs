using Godot;
using LibreKO.Features.PluginBridge;
using LibreKO.Features.Controls;
using LibreKO.Features.CombatLog;

namespace LibreKO.Scene;

public partial class WorldRoot : Node3D, IWorldContext, IHudLayout
{
    internal int MyId => Avatar.CharId;
    internal int ZoneId { get; private set; }
    internal bool WorldReady { get; private set; }

    internal void EnterAs(int charId, int zoneId)
    {
        Avatar.CharId = charId;
        ZoneId = zoneId;
    }

    internal ChatSystem Chat { get; set; } = null!;
    internal FloaterSystem Floaters { get; private set; } = null!;
    internal CombatLogFeature Log { get; private set; } = null!;
    internal PluginBridgeFeature PluginBridge { get; private set; } = null!;
    internal HotkeysFeature Hotkeys { get; private set; } = null!;
    internal PlayerInputFeature PlayerInput { get; private set; } = null!;
    internal CommandListFeature CommandList { get; private set; } = null!;
    internal ChatHostFeature ChatHost { get; private set; } = null!;
    internal PlayerMenuFeature PlayerMenu { get; private set; } = null!;
    internal ItemRoutingFeature ItemRouting { get; private set; } = null!;
    internal NpcInteractionFeature NpcInteraction { get; private set; } = null!;
    internal DebugInfoFeature DebugInfo { get; private set; } = null!;
    internal GameMenuFeature GameMenu { get; private set; } = null!;
    internal MainWindowsFeature MainWindows { get; private set; } = null!;
    internal MapFeature Map { get; private set; } = null!;
    internal HudLauncherFeature HudLauncher { get; private set; } = null!;
    internal StatusHudFeature StatusHud { get; private set; } = null!;
    internal AudioSystem AudioSystem { get; private set; } = null!;
    internal ScenerySystem ScenerySystem { get; private set; } = null!;
    internal MapInteractablesSystem MapInteractablesSystem { get; private set; } = null!;
    internal GateSystem GateSystem { get; private set; } = null!;
    internal SelfAvatar Avatar { get; } = new();
    internal PlayerSystem PlayerSystem { get; private set; } = null!;
    internal TouchControlsSystem TouchControlsSystem { get; private set; } = null!;
    internal SiegeTowerSystem SiegeTowerSystem { get; private set; } = null!;
    internal CameraSystem CameraSystem { get; private set; } = null!;
    internal SkillCastSystem SkillCastSystem { get; private set; } = null!;
    internal BasicAttackSystem BasicAttackSystem { get; private set; } = null!;
    internal CombatSystem CombatSystem { get; private set; } = null!;
    internal EntityActionSystem EntityActionSystem { get; private set; } = null!;
    internal CombatFxSystem CombatFxSystem { get; private set; } = null!;
    internal SkillMovementSystem SkillMovementSystem { get; private set; } = null!;
    internal BuffSystem BuffSystem { get; private set; } = null!;
    internal SkillAreaSystem SkillAreaSystem { get; private set; } = null!;
    internal SoccerSystem SoccerSystem { get; private set; } = null!;
    internal GatherSystem GatherSystem { get; private set; } = null!;
    internal EntitySystem EntitySystem { get; private set; } = null!;
    internal EntityStatesSystem EntityStatesSystem { get; private set; } = null!;
    internal EntityPlatesSystem EntityPlatesSystem { get; private set; } = null!;
    internal EntityLooksSystem EntityLooksSystem { get; private set; } = null!;
    internal StealthSystem StealthSystem { get; private set; } = null!;
    internal SelectionFeature Selection { get; private set; } = null!;
    internal CursorFeature Cursor { get; private set; } = null!;
    internal HpStatusFeature HpStatus { get; private set; } = null!;
    internal TargetHudFeature TargetHud { get; private set; } = null!;
    internal GenieFeature Genie { get; private set; } = null!;
    internal GenieSubFeature GenieSub { get; private set; } = null!;
    internal GenieTownFeature GenieTown { get; private set; } = null!;
    internal GenieScrollsFeature GenieScrolls { get; private set; } = null!;
    internal GeniePotionsFeature GeniePotions { get; private set; } = null!;
    internal GenieHammerFeature GenieHammer { get; private set; } = null!;
    internal NpcDialogFeature NpcDialogWindow { get; private set; } = null!;
    internal InventoryFeature InventoryWindow { get; private set; } = null!;
    internal ItemTooltipFeature ItemTooltip { get; private set; } = null!;
    internal LootFeature Loot { get; private set; } = null!;
    internal WarpFeature Warp { get; private set; } = null!;
    internal VanguardFeature VanguardWindow { get; private set; } = null!;
    internal ClanMatchFeature ClanMatchWindow { get; private set; } = null!;
    internal AdminPanelFeature AdminPanel { get; private set; } = null!;
    internal ClanPointsFeature ClanPoints { get; private set; } = null!;
    internal ClanFeature Clan { get; private set; } = null!;
    internal SiegeFeature Siege { get; private set; } = null!;
    internal MerchantFeature Merchant { get; private set; } = null!;
    internal MerchantSearchFeature MerchantSearch { get; private set; } = null!;
    internal MerchantBuyFeature MerchantBuy { get; private set; } = null!;
    internal MarketPriceFeature MarketPrice { get; private set; } = null!;
    internal MerchantStallsFeature MerchantStalls { get; private set; } = null!;
    internal PartyInvitesFeature PartyInvites { get; private set; } = null!;
    internal PartyFeature Party { get; private set; } = null!;
    internal PartyCommanderFeature PartyCommander { get; private set; } = null!;
    internal SeekPartyFeature SeekParty { get; private set; } = null!;
    internal PartyWarBoardFeature WarBoard { get; private set; } = null!;
    internal QuestFeature Quest { get; private set; } = null!;
    internal QuestTrackerFeature QuestTracker { get; private set; } = null!;
    internal QuestMarkersFeature QuestMarkers { get; private set; } = null!;
    internal QuestTargetFeature QuestTarget { get; private set; } = null!;
    internal QuestReceiptFeature QuestReceipt { get; private set; } = null!;
    internal StatsFeature Stats { get; private set; } = null!;
    internal ProgressionFeature Progression { get; private set; } = null!;
    internal SkillWindowFeature SkillWindow { get; private set; } = null!;
    internal SkillPresetFeature SkillPreset { get; private set; } = null!;
    internal HotbarFeature Hotbar { get; private set; } = null!;
    internal ChaosSkillWindowFeature ChaosSkillWindow { get; private set; } = null!;
    internal PetSkillBarFeature PetSkillBar { get; private set; } = null!;
    internal PotionBarFeature PotionBar { get; private set; } = null!;
    internal ClanBattleWarFeature ClanBattleWar { get; private set; } = null!;
    internal TownRecallFeature TownRecall { get; private set; } = null!;
    internal PortraitFeature Portrait { get; private set; } = null!;
    internal RenderFaultFeature RenderFault { get; private set; } = null!;
    internal ZoneAbilityFeature ZoneAbility { get; private set; } = null!;
    internal CosmeticsFeature Cosmetics { get; private set; } = null!;
    internal TargetQuestDropsFeature TargetQuestDrops { get; private set; } = null!;
    internal RankerStatueFeature RankerStatueWindow { get; private set; } = null!;
    internal NpcGatesFeature NpcGates { get; private set; } = null!;
    internal TargetEffectsFeature TargetEffects { get; private set; } = null!;
    internal GmFxFeature GmFx { get; private set; } = null!;
    internal HitFlashFeature HitFlash { get; private set; } = null!;
    internal ControlStateFeature ControlState { get; private set; } = null!;
    internal UserInfoFeature UserInfo { get; private set; } = null!;
    internal KnightRoyaleFeature KnightRoyaleWindow { get; private set; } = null!;
    internal ClanBoardFeature ClanBoard { get; private set; } = null!;
    internal WishBoardFeature WishBoard { get; private set; } = null!;
    internal PetFeature Pet { get; private set; } = null!;
    internal CapeFeature CapeWindow { get; private set; } = null!;
    internal NpcFinderFeature NpcFinderWindow { get; private set; } = null!;
    internal NereidFeature Nereid { get; private set; } = null!;
    internal LootFilterFeature LootFilterWindow { get; private set; } = null!;
    internal RankBoardFeature RankBoardWindow { get; private set; } = null!;
    internal ClanMarksFeature ClanMarks { get; private set; } = null!;
    internal RankerMessageFeature RankerMessageWindow { get; private set; } = null!;
    internal ShoppingMallFeature ShoppingMall { get; private set; } = null!;
    internal PowerUpStoreGiftFeature PowerUpStoreGift { get; private set; } = null!;
    internal ChaosFeature Chaos { get; private set; } = null!;
    internal ChatRoomFeature ChatRoom { get; private set; } = null!;
    internal ChatReactionsFeature ChatReactions { get; private set; } = null!;
    internal WhispersFeature Whispers { get; private set; } = null!;
    internal NationChannelsFeature NationChannels { get; private set; } = null!;
    internal EventTitlesFeature EventTitlesWindow { get; private set; } = null!;
    internal EffectFeature Effect { get; private set; } = null!;
    internal FriendsFeature Friends { get; private set; } = null!;
    internal MonsterStoneFeature MonsterStone { get; private set; } = null!;
    internal AngerGaugeFeature AngerGaugeWindow { get; private set; } = null!;
    internal DisguiseFeature DisguiseWindow { get; private set; } = null!;
    internal RebirthFeature Rebirth { get; private set; } = null!;
    internal CollectionRaceFeature CollectionRace { get; private set; } = null!;
    internal QuestNotificationsFeature QuestNotifications { get; private set; } = null!;
    internal ChangeHairFeature ChangeHair { get; private set; } = null!;
    internal BorderDefenseWarFeature BorderDefenseWar { get; private set; } = null!;
    internal PvpFeature Pvp { get; private set; } = null!;
    internal ChallengeFeature Challenge { get; private set; } = null!;
    internal AttendanceFeature Attendance { get; private set; } = null!;
    internal EventsFeature Events { get; private set; } = null!;
    internal TimedItemsFeature TimedItemsWindow { get; private set; } = null!;
    internal PremiumFeature Premium { get; private set; } = null!;
    internal ResurrectOfferFeature ResurrectOffer { get; private set; } = null!;
    internal DailyRankFeature DailyRankWindow { get; private set; } = null!;
    internal ClassChangeFeature ClassChange { get; private set; } = null!;
    internal KingElectionFeature KingElection { get; private set; } = null!;
    internal KingTreasuryFeature KingTreasury { get; private set; } = null!;
    internal MailIconFeature MailIcon { get; private set; } = null!;
    internal MailFeature Mail { get; private set; } = null!;
    internal MailComposeFeature MailCompose { get; private set; } = null!;
    internal ExchangeFeature Exchange { get; private set; } = null!;
    internal ChaosGuideFeature ChaosGuide { get; private set; } = null!;
    internal ItemCombineFeature ItemCombineWindow { get; private set; } = null!;
    internal ItemSmashFeature ItemSmashWindow { get; private set; } = null!;
    internal PieceChangeFeature PieceChange { get; private set; } = null!;
    internal UpgradeFeature Upgrade { get; private set; } = null!;
    internal RepairFeature Repair { get; private set; } = null!;
    internal ClanWarehouseFeature ClanWarehouse { get; private set; } = null!;
    internal VipWarehouseFeature VipWarehouse { get; private set; } = null!;
    internal WarehouseFeature Warehouse { get; private set; } = null!;
    internal CharacterSealFeature CharacterSealWindow { get; private set; } = null!;
    internal SealFeature Seal { get; private set; } = null!;
    internal BagCompanionFeature BagCompanions { get; private set; } = null!;
    internal VendorFeature Vendor { get; private set; } = null!;
    internal QuestProgressToastFeature QuestProgressToast { get; private set; } = null!;
    internal BifrostFeature Bifrost { get; private set; } = null!;
    internal AchievementTrophyFeature AchievementTrophy { get; private set; } = null!;
    internal TitlePickerFeature TitlePicker { get; private set; } = null!;
    internal AchievementFeature Achievement { get; private set; } = null!;
    internal ClanBattleFeature ClanBattle { get; private set; } = null!;
    internal BattleEventFeature BattleEvent { get; private set; } = null!;
    internal ShoutFeature Shout { get; private set; } = null!;
    internal EventPlatesFeature EventPlates { get; private set; } = null!;
    internal EntryPopupsFeature EntryPopups { get; private set; } = null!;
    internal ChatNoticesFeature ChatNotices { get; private set; } = null!;
    internal LotteryFeature Lottery { get; private set; } = null!;
    internal UiOpacityFeature UiOpacity { get; private set; } = null!;
    internal SeasonalFeature SeasonalWindow { get; private set; } = null!;
    internal NationTransferFeature NationTransfer { get; private set; } = null!;
    internal DeathDialogFeature DeathDialog { get; private set; } = null!;
    internal TopIconsFeature TopIcons { get; private set; } = null!;
    internal RepurchaseFeature Repurchase { get; private set; } = null!;
    internal UserVoteFeature UserVoteWindow { get; private set; } = null!;
    internal EquipViewFeature EquipView { get; private set; } = null!;
    internal ExpSealFeature ExpSealWindow { get; private set; } = null!;
    internal SlaveMerchantFeature SlaveMerchantWindow { get; private set; } = null!;
    internal SpecialAuctionFeature SpecialAuctionWindow { get; private set; } = null!;
    internal ExpBarFeature? ExpBar { get; set; }
    internal MsgOutFeature MsgOut { get; private set; } = null!;
    internal RankFeature Rank { get; private set; } = null!;
    internal UpgradeNoticeFeature UpgradeNotice { get; private set; } = null!;
    internal PetHatchFeature PetHatch { get; private set; } = null!;
    internal FortuneFeature Fortune { get; private set; } = null!;
    internal YutGameFeature Yut { get; private set; } = null!;
    internal DrakiBoardFeature DrakiBoard { get; private set; } = null!;
    internal NameChangeFeature NameChange { get; private set; } = null!;
    internal GenderChangeFeature GenderChangeWindow { get; private set; } = null!;
    internal AwakenFeature Awaken { get; private set; } = null!;
    internal ResetConfirmFeature ResetConfirm { get; private set; } = null!;
    internal GuardPetFeature GuardPet { get; private set; } = null!;
    internal ClanPremiumFeature ClanPremium { get; private set; } = null!;
    internal ClanHandoverFeature ClanHandover { get; private set; } = null!;
    internal ClanCreateFeature ClanCreate { get; private set; } = null!;
    internal KrowazFeature Krowaz { get; private set; } = null!;
    internal KillSplashFeature KillSplash { get; private set; } = null!;
    internal RentalFeature Rental { get; private set; } = null!;
    internal GlobalMapFeature GlobalMap { get; private set; } = null!;
    internal FishingHallFeature FishingHall { get; private set; } = null!;
    internal RouletteFeature Roulette { get; private set; } = null!;
    internal ForcesFeature Forces { get; private set; } = null!;
    internal MessengerFeature Messenger { get; private set; } = null!;
    internal TournamentFeature Tournament { get; private set; } = null!;
    internal ReportFeature Report { get; private set; } = null!;
    internal BountyFeature Bounty { get; private set; } = null!;

    Node3D IWorldContext.Root => this;
    double IWorldContext.Now => FrameClock.Now();
    int IWorldContext.Zone => ZoneId;
    bool IWorldContext.Ready => WorldReady;
    bool IWorldContext.Loading => loadingLayer != null;
    ChatSystem IWorldContext.Chat => Chat;
    FloaterSystem IWorldContext.Floaters => Floaters;
    ICombatLog IWorldContext.Log => Log;
    IItemTooltips IWorldContext.Tooltips => ItemTooltip;
    void IHudLayout.PluginSeam(CanvasLayer layer, HudPart part) => PluginHudSeam(layer, part);
    IInventoryView IWorldContext.Inventory => InventoryWindow;
    List<Rect2> IHudLayout.DockRects() => DockHudRects();
    Control? IHudLayout.StatusHud => StatusHud.Root;
    Control? IHudLayout.PluginPart(HudPart part) => pluginHud.TryGetValue(part, out var control) ? control : null;
    INpcDialog IWorldContext.NpcDialog => NpcDialogWindow;
    IMainWindows IWorldContext.MainWindows => MainWindows;
    IBagCompanions IWorldContext.BagCompanions => BagCompanions;
    bool IWorldContext.Alive => Alive;
    void IHudLayout.RefreshVitalBars() => StatusHud.RefreshVitalBars();
    void IHudLayout.RefreshLevelOrb()
    {
        StatusHud.RefreshLevelOrb();
        ExpBar?.Update();
    }
    IMovement IWorldContext.Movement => PlayerSystem;
    Camera3D? IWorldContext.Camera => CameraSystem.Camera;
    Vector2 IHudLayout.VitalsSize => StatusHudFeature.VitalsSize;
    TouchActionBar? IHudLayout.TouchActions => TouchControlsSystem.TouchActions;
    ISelfView IWorldContext.Self => PlayerSystem;
    IEntityAccess IWorldContext.Entities => EntitySystem;
    ISelection IWorldContext.Selection => Selection;
    IHudLayout IWorldContext.Hud => this;
    IPluginNotices IWorldContext.Plugins => PluginBridge;
    ITerrainQueries IWorldContext.Terrain => ScenerySystem;

    internal bool Alive => IsInstanceValid(this) && !IsQueuedForDeletion();

    public override void _Ready()
    {
        GD.Print("[enter] world scene instantiated");
        Game.Connection.WorldActive = true;
        _ = Diag.Guard("world-load", LoadWorld);
    }

    private double loadStartedAt;
    private LoadingScreen? loadingLayer;

    private async System.Threading.Tasks.Task LoadWorld()
    {
        Ui.MenuScale(false);
        Game.Session.QuestBook.Clear();
        Game.Session.Achievement.Clear();
        GmItemId.Shown = Game.Session.Self.IsGm;
        PluginHost.SweepUnloaded();
        BuildCoreSystems();
        EntitySystem.AttachLayer(GetNode<Node3D>("Entities"));
        CameraSystem.Attach(GetNode<Camera3D>("Camera"));

        var info = Game.Session.Self.LastEnter;
        EnterAs(info.CharId, info.Zone);
        Avatar.MapX = info.X; Avatar.MapZ = info.Z; Avatar.MapY = info.Y;
        PlayerSystem.ApplyAuthority(info.Authority == SelfSession.GameMasterAuthority, Game.Session.Self.GmCollisionsOff);

        loadStartedAt = FrameClock.Now();
        BuildLoading();
        GlyphWarmer.Warm(this);
        if (!await LoadStep("Preparing…", 0.04f, "Lighting and sky")) return;

        ScenerySystem.BuildEnvironment();
        Config.ApplyGraphicsToViewport();
        Cape.Enabled = Config.Capes;

        string zoneLabel = $"Zone {info.Zone}";
        foreach (var z in ZoneCatalog.All) if (z.Id == info.Zone) { zoneLabel = $"{z.Name} (zone {z.Id})"; break; }
        if (!await LoadStep("Loading terrain…", 0.15f, zoneLabel)) return;

        bool hasTerrain = ScenerySystem.BuildTerrain(info.Zone);
        var ground = GetNodeOrNull<MeshInstance3D>("Ground");
        if (hasTerrain) ground?.QueueFree();
        else if (ground != null) ground.Position = Coord.ToGodot(info.X, 0, info.Z);
        FxBillboard.GroundHeight = hasTerrain ? ScenerySystem.FxGroundHeight : null;
        if (!await LoadStep("Loading world objects…", 0.45f,
            hasTerrain ? "Objects · collision · water · map FX" : "Flat placeholder (zone not baked)")) return;

        if (hasTerrain)
        {
            GD.Print("[load] world objects: placements");
            await ScenerySystem.BuildObjects();

            if (!Alive) return;
            GD.Print("[load] world objects: collision");
            ScenerySystem.BuildObjectCollision();
            ScenerySystem.RegroundEntities();
            GD.Print("[load] world objects: water");
            ScenerySystem.BuildWater();
            GD.Print("[load] world objects: ambient FX");
            ScenerySystem.BuildFxPlacements();
            NpcRoleFx.Warm(this);
            GD.Print("[load] world objects: complete");
        }
        if (!await LoadStep("Loading character…", 0.70f,
            $"{(info.Name.Length > 0 ? info.Name : "You")} · Lv {Game.Session.Self.Sheet.Level} · {CharacterClassCatalog.DisplayName(info.Class)}")) return;

        PlayerSystem.BuildSelf(info);

        if (!await LoadStep("Entering world…", 0.92f, "Spawning nearby players & monsters")) return;

        EntitySystem.Attach();
        Selection.Attach();
        CombatSystem.Attach();

        Game.Session.Entities.Replay(EntitySystem.OnSpawn);
        EntityStatesSystem.ApplySelfPlateState();

        BuildFacets();
        EntityLooksSystem.DressSelf();
        Cosmetics.RefreshRankAuraFor(MyId, StealthSystem.StealthIds.Contains(MyId));
        if (Game.Session.Progress.SelfTransformSkill > 0 && !EntityStatesSystem.ZoneLookHolds(MyId)) EntityStatesSystem.WearMonsterLook(MyId, Game.Session.Progress.SelfTransformSkill);
        else
        {
            if (Game.Session.Progress.SelfTransformSkill > 0) EntityStatesSystem.HoldSelfTransform(Game.Session.Progress.SelfTransformSkill);
            EntityStatesSystem.ApplyZoneLook(MyId);
        }
        if (Game.Session.Progress.SelfDevilForm) EntityStatesSystem.ApplyDevilForm(MyId, true);
        BuildEscapeOrder();
        ApplyHudTheme();
        GlyphWarmer.Warm(this);
        CharacterPreview.Warm();
        Floaters?.WarmText();

        if (Avatar.Anim != null) { ModelAnimation.PlayClipOn(Avatar.Anim, ref Avatar.Clip, "idle"); Avatar.Anim.Advance(0.0); }
        WorldReady = true;
        Game.Connection.AcknowledgeWorldLoaded();

        var fxWarm = Hotbar.WarmFxInView();
        if (!await LoadStep("Syncing server time…", 0.96f, $"{EntitySystem.Ents.Count} entities nearby")) return;
        await WaitForServerEnvironment(2.5);

        if (!Alive) return;

        if (!await LoadStep("Ready", 1.0f, "Welcome to LibreKO")) return;
        MemoryCensus.Log($"zone {ZoneId} loaded");
        if (fxWarm != null && IsInstanceValid(fxWarm)) fxWarm.QueueFree();
        HideLoading();
        Bifrost.RefreshInZoneLeaveUi();
        ChaosGuide.ShowOnEntry();
        SeekParty.ShowFullMoonCaptionOnEntry();
        EntryPopups.Release();
        Bifrost.RequestTime();
    }

    private async System.Threading.Tasks.Task WaitForServerEnvironment(double timeoutSec)
    {
        double waited = 0.0;
        while (Game.Session.Zone.LastTime is null && waited < timeoutSec)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!Alive) return;
            waited += GetProcessDeltaTime();
        }
        if (Game.Session.Zone.LastTime is { } t) ScenerySystem.OnServerTime(t.Hour, t.Minute);
    }

    public override void _Input(InputEvent ev)
    {
        if (!WorldReady || Game.Connection.Reconnect.Blocking) return;
        if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right }
            && !Chat.IsActive)
            ReleaseTextFocusOutside();
        if (ev is InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Left,
            } leftPress)
        {
            PlayerSystem.CancelAutoRun();
            Whispers.FocusWhisperAt(leftPress.Position);
        }

        if (ev is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (Chat.IsActive)
            {
                if (k.Keycode == Key.Escape) { Chat.Close(); GetViewport().SetInputAsHandled(); }
                return;
            }
            if (k.Keycode == Key.Escape && Whispers.TryMinimizeFocusedWhisper())
            { GetViewport().SetInputAsHandled(); return; }
            if (k.Keycode == Key.Escape && FocusedTextField() is { } textField)
            { textField.ReleaseFocus(); GetViewport().SetInputAsHandled(); return; }
            if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox) return;
            if (k.Keycode is Key.Enter or Key.KpEnter && Warp.TryEnter()) { GetViewport().SetInputAsHandled(); return; }
            if (k.Keycode is Key.Enter or Key.KpEnter) { Chat.Open(); GetViewport().SetInputAsHandled(); return; }
            PlayerSystem.NoteMoveKey(k);
            if (Hotkeys.TryPress(k))
            { GetViewport().SetInputAsHandled(); return; }
            if (k.Keycode == Key.Escape)
            { HandleEscape(); GetViewport().SetInputAsHandled(); }
        }
    }

    private Control? FocusedTextField() =>
        GetViewport().GuiGetFocusOwner() is { } focused and (LineEdit or TextEdit) ? focused : null;

    private void ReleaseTextFocusOutside()
    {
        if (FocusedTextField() is not { } focused) return;
        if (focused.GetGlobalRect().HasPoint(focused.GetGlobalMousePosition())) return;
        focused.ReleaseFocus();
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (!WorldReady) return;
        if (ev is InputEventJoypadButton { Pressed: true } jb)
        {
            if (Hotkeys.TryPressPad(jb.ButtonIndex)) GetViewport().SetInputAsHandled();
            return;
        }
        if (ev is InputEventMouseButton mb)
        {
            bool overUi = GetViewport().GuiGetHoveredControl() != null;
            float maxDist = DebugInfo.IsOpen ? CameraSystem.InfoMaxDist : CameraSystem.MaxDist;
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp && !overUi)
                CameraSystem.ZoomIn(maxDist);
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown && !overUi)
                CameraSystem.ZoomOut(maxDist);
            else if (Exchange.TradeHoldsInput) return;
            else if (PlayerInput.HandleProfilePointer(mb, overUi)) return;
        }
        else if (Exchange.TradeHoldsInput) return;
        else if (ev is InputEventMouseMotion mm)
            PlayerInput.HandleProfilePointer(mm, GetViewport().GuiGetHoveredControl() != null);
    }

    public override void _Process(double delta)
    {
        if (!WorldReady) return;
        using var worldScope = Perf.Measure(Perf.Section.World);
        if (!Game.Connection.Reconnect.Blocking)
        {
            double step = FrameStep.Clamp(delta);
            PlayerSystem.UpdateHeldMoveTarget(step);
            PlayerSystem.HandleInput(step);
        }

        using (Perf.Measure(Perf.Section.Spawn))
        {
            EntitySystem.ProcessSpawnQueue();
            EntityLooksSystem.GearStreamTick();
        }

        float dt = (float)delta;
        double nowSec = FrameClock.Now();
        Vector3 selfPos = Avatar.Body.Position;
        Vector3 camPos = CameraSystem.Camera != null ? CameraSystem.Camera.GlobalPosition : selfPos;
        FxLod.Update(camPos, delta);
        var entityScope = Perf.Measure(Perf.Section.Entities);
        StealthSystem.StealthTick(selfPos);
        EntityStatesSystem.RespawnBlinkTick(nowSec);
        EntityStatesSystem.SizeStateTick(nowSec);
        EntitySystem.RefreshFrustum();
        EntitySystem.RankEntityAnimation();
        PlateFollow.FollowHeadJoint(Avatar.Body, EntityPlatesSystem.SelfPlateRoot(), EntityPlatesSystem.SelfHead);
        SoccerSystem.SoccerTick(delta);
        PlayerSystem.GestureTick();
        GateSystem.KeyGateTick(delta);
        PlayerSystem.NpcTouchTick(delta);
        EntityActionSystem.TickSelfFacing(dt);
        Features.Tick(delta, nowSec);
        EntitySystem.TickEntities(delta, nowSec, selfPos, camPos);
        entityScope.Dispose();

        var selfScope = Perf.Measure(Perf.Section.SelfAnim);
        PlayerSystem.TickSelfAnimation(nowSec);

        EntityLooksSystem.WingTick();
        CombatSystem.StanceTick();
        SkillCastSystem.ApplySelfAnimSpeed();
        AudioSystem.FootstepTick();
        selfScope.Dispose();

        using (Perf.Measure(Perf.Section.Combat))
        {
            CombatSystem.Update(nowSec);
            SkillAreaSystem.AreaCastTick(delta, nowSec);
            Features.LateTick(delta, nowSec);
            GatherSystem.GatherTick(nowSec);
            TouchControlsSystem.TouchInteractionTick(delta);
            Chat.Bubbles.Tick(nowSec);
            SkillCastSystem.CastMoveCancelTick();
            MapInteractablesSystem.WarpGates.Tick();
            MapInteractablesSystem.Anvils.Tick();
            TextureAnim.Tick(delta);
        }

        using (Perf.Measure(Perf.Section.Selection))
        {
            Selection.UpdateRing();
            TargetHud.TargetHpPollTick(nowSec);
            TargetEffects.PollTick(nowSec);
            TargetHud.UpdateTargetHud();
            TargetEffects.TickTargetEffects();
        }

        using (Perf.Measure(Perf.Section.Camera)) CameraSystem.UpdateCamera(delta);

        using (Perf.Measure(Perf.Section.Hud))
        {
            Floaters?.Tick(nowSec);
            Cursor.CursorTick(delta);
            Features.HudTick(delta, nowSec);
        }

        using (Perf.Measure(Perf.Section.Sky)) ScenerySystem.UpdateSky(delta);

        using (Perf.Measure(Perf.Section.MapFx))
        {
            ScenerySystem.MapFxCullTick(delta);
            Selection.PickMarkerTick(delta);
        }
    }

    public override void _ExitTree()
    {
        Game.Connection.WorldActive = false;
        GameCursor.Disable();
        TeardownFacets();
        ModelNodes.ForgetKeptModels();
    }

    private void BuildLoading()
    {
        loadingLayer = new LoadingScreen();
        AddChild(loadingLayer);
    }

    private async System.Threading.Tasks.Task<bool> LoadStep(string status, double progress, string detail = "")
    {
        Diag.Phase = $"world-load {progress * 100.0:0}% {status}";
        GD.Print($"[load] {progress * 100.0:0}% {status} (+{FrameClock.Now() - loadStartedAt:0.0}s)" +
                 (string.IsNullOrWhiteSpace(detail) ? "" : $" — {detail}"));
        loadingLayer?.Set(status, progress, detail);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        return Alive;
    }

    private void HideLoading()
    {
        loadingLayer?.QueueFree();
        loadingLayer = null;
        Backdrop.RerollLoadingArt();
    }

    // Order is load-bearing: later features read what earlier ones built. Teardown runs in reverse.
    internal FeatureRegistry Features { get; } = new();

    private void Facet(Action init, Action? teardown = null) => Features.Add(init, teardown);

    internal void BuildCoreSystems()
    {
        PluginBridge = new PluginBridgeFeature(this, Game.Session.Self);
        ScenerySystem = new ScenerySystem(this, Game.Session.Zone, Avatar);
        CameraSystem = new CameraSystem(this, Avatar, ScenerySystem);
        StealthSystem = new StealthSystem(this, Game.Session.Stealth, Game.Requests.Stealth);
        EntityLooksSystem = new EntityLooksSystem(this, Game.Session.Cosmetics, Game.Session.Self, Avatar, Game.Session.GmFx);
        EntityPlatesSystem = new EntityPlatesSystem(this, Game.Session.Clan, Game.Session.Self, Game.Session.Siege, Avatar, Game.Session.Party, Game.Session.Friends, EntityLooksSystem, PluginBridge);
        EntityActionSystem = new EntityActionSystem(this, Game.Session.Genie, Avatar, EntityLooksSystem, ScenerySystem);
        EntityStatesSystem = new EntityStatesSystem(this, Game.Session.Achievement, Game.Session.Clan, Game.Session.Genie, Game.Session.Progress, Game.Session.Self, Game.Requests.Player, Game.Requests.Soccer, Avatar, EntityLooksSystem, EntityActionSystem, StealthSystem, EntityPlatesSystem);
        AudioSystem = new AudioSystem(this, Game.Session.Self, Avatar, EntityActionSystem, ScenerySystem, EntityLooksSystem, EntityStatesSystem);
        EntitySystem = new EntitySystem(this, Game.Session.Clan, Game.Session.GmFx, Game.Session.Self, Game.Session.Entities, Game.Session.Items, Game.Session.Achievement, Avatar, EntityPlatesSystem, AudioSystem, EntityActionSystem, EntityStatesSystem, EntityLooksSystem, StealthSystem, ScenerySystem);
        BuffSystem = new BuffSystem(this, Game.Session.Combat, Game.Session.Self, Game.Session.Zone, Game.Requests.Combat, Game.Session.Progress, EntityStatesSystem);
        CombatFxSystem = new CombatFxSystem(this, EntityActionSystem, EntitySystem, AudioSystem, EntityLooksSystem, EntityStatesSystem);
        SoccerSystem = new SoccerSystem(this, Game.Session.Progress, Game.Session.Self, Game.Session.Soccer, Game.Requests.Soccer, Avatar, ScenerySystem, EntitySystem);
        MapInteractablesSystem = new MapInteractablesSystem(this, Avatar, Game.Requests.ObjectEvent, ScenerySystem);
        GateSystem = new GateSystem(this, Game.Session.ObjectEvent, Game.Requests.ObjectEvent, Avatar, ScenerySystem, CameraSystem, MapInteractablesSystem, Game.Session.Entities);
        SiegeTowerSystem = new SiegeTowerSystem(this, Game.Session.Clan, Game.Session.Self, Game.Session.Siege, Game.Requests.Siege, Avatar, ScenerySystem);
        SkillCastSystem = new SkillCastSystem(this, AudioSystem, Avatar, BuffSystem, CombatFxSystem, EntityActionSystem, EntityLooksSystem, EntityStatesSystem, GateSystem, Game.Session.Party, Game.Session.Pvp, Game.Requests.Combat, ScenerySystem, Game.Session.Self, SiegeTowerSystem, Game.Session.SkillTimers, SoccerSystem, EntitySystem);
        BasicAttackSystem = new BasicAttackSystem(this, AudioSystem, Avatar, BuffSystem, EntityActionSystem, EntityLooksSystem, EntityStatesSystem, Game.Requests.Combat, Game.Session.Self, SkillCastSystem, SoccerSystem);
        CombatSystem = new CombatSystem(this, Game.Session.Combat, Game.Session.Items, Game.Session.Self, Game.Session.Vendor, Game.Requests.Player, Avatar, AudioSystem, EntitySystem, EntityActionSystem, EntityStatesSystem, EntityPlatesSystem, BuffSystem, SkillCastSystem, BasicAttackSystem);
        SkillMovementSystem = new SkillMovementSystem(this, Avatar, ScenerySystem, EntityActionSystem, CombatFxSystem, SkillCastSystem);
        PlayerSystem = new PlayerSystem(this, Game.Session.Admin, Game.Session.Clan, Game.Session.Self, Game.Requests.NpcQuest, Game.Requests.Player, Game.Session.Cosmetics, Avatar, CombatSystem, EntityLooksSystem, ScenerySystem, EntitySystem, EntityPlatesSystem, SkillMovementSystem, EntityStatesSystem, EntityActionSystem, Game.Session.Zone, CameraSystem, BuffSystem, Game.Requests.Soccer, SkillCastSystem, BasicAttackSystem);
        SkillAreaSystem = new SkillAreaSystem(this, Game.Session.Self, ScenerySystem, EntityActionSystem, PlayerSystem, SkillCastSystem);
        GatherSystem = new GatherSystem(this, Game.Session.Mining, Game.Session.Self, Game.Requests.Mining, Avatar, EntityActionSystem, AudioSystem, CombatFxSystem, EntitySystem);
        Selection = new SelectionFeature(this, Game.Session.Party, Game.Session.Self, Game.Session.Entities, Avatar, ScenerySystem, BuffSystem, MapInteractablesSystem, EntityActionSystem, PlayerSystem, SkillCastSystem, BasicAttackSystem);
        ItemRouting = new ItemRoutingFeature(this, this, Game.Session.Self);
        NpcInteraction = new NpcInteractionFeature(this, this, Game.Session.NpcQuest, Game.Session.Warehouse, Game.Requests.NpcQuest, Game.Requests.Warp, Selection, MapInteractablesSystem, GateSystem, EntityActionSystem);
        PlayerMenu = new PlayerMenuFeature(this, this, Game.Session.Clan, Game.Session.Hostility, Game.Session.Party, Game.Requests.Clan, Game.Requests.ClanBattleWar, Game.Requests.Party);
        TouchControlsSystem = new TouchControlsSystem(this, this, CameraSystem, CombatSystem, Selection, NpcInteraction, MapInteractablesSystem);
        Features.Add(EntitySystem);
        Features.Add(EntityStatesSystem);
        Features.Add(EntityPlatesSystem);
        Features.Add(EntityLooksSystem);
        Features.Add(StealthSystem);
        Features.Add(SkillCastSystem);
        Features.Add(BasicAttackSystem);
        Features.Add(CombatSystem);
        Features.Add(EntityActionSystem);
        Features.Add(CombatFxSystem);
        Features.Add(SkillMovementSystem);
        Features.Add(BuffSystem);
        Features.Add(SkillAreaSystem);
        Features.Add(SoccerSystem);
        Features.Add(GatherSystem);
        Features.Add(PlayerSystem);
        Features.Add(TouchControlsSystem);
        Features.Add(SiegeTowerSystem);
        Features.Add(CameraSystem);
        Features.Add(ScenerySystem);
        Features.Add(MapInteractablesSystem);
        Features.Add(GateSystem);
        Features.Add(Selection);
        Features.BuildPending();
    }

    internal void BuildFacets()
    {
        Features.Add(StatusHud = new StatusHudFeature(this, Game.Session.Self, CombatSystem));
        Features.Add(TopIcons = new TopIconsFeature(this, () => ShoppingMall.OpenPowerUpStore(), () => Lottery.Toggle(), () => Genie.Toggle()));
        Features.Add(EntryPopups = new EntryPopupsFeature(this));
        Facet(BuffSystem.BuildBuffBar);
        Features.Add(Map = new MapFeature(this, Game.Session.QuestBook, Game.Requests.BattleEvent, ScenerySystem, BuffSystem, CameraSystem, PluginBridge));
        Features.Add(NpcFinderWindow = new NpcFinderFeature(this, Game.Session.Self, Map));
        Facet(ScenerySystem.BuildClockHud);
        Facet(Selection.BuildRing);
        Facet(Selection.BuildObjectHighlight);
        Features.Add(UpgradeNotice = new UpgradeNoticeFeature(this, Game.Session.UpgradeNotice));
        Features.Add(Effect = new EffectFeature(this, Game.Session.Effect, UpgradeNotice));
        Features.Add(TargetEffects = new TargetEffectsFeature(this, Game.Session.TargetState, Game.Requests.TargetState, Effect));
        Features.Add(LootFilterWindow = new LootFilterFeature(this, Game.Session.Self, Game.Session.TargetState, Game.Requests.TargetState));
        Features.Add(TargetQuestDrops = new TargetQuestDropsFeature(this, Game.Session.Combat));
        ClanMarks = new ClanMarksFeature(this, Game.Session.Clan, Game.Session.Self, Game.Requests.Clan, EntitySystem);
        Features.Add(TargetHud = new TargetHudFeature(this, Game.Requests.Combat, SiegeTowerSystem, EntityPlatesSystem, EntitySystem, TargetEffects, TargetQuestDrops, LootFilterWindow, ClanMarks, Selection, PluginBridge));
        Features.Add(CapeWindow = new CapeFeature(this, Game.Session.Cape, Game.Session.Clan, Game.Session.GmFx, Game.Session.Self, Game.Requests.Cape, EntityLooksSystem, NpcInteraction));
        Features.Add(DebugInfo = new DebugInfoFeature(this, Selection, CapeWindow, ScenerySystem, CameraSystem));
        Features.Add(GameMenu = new GameMenuFeature(this, Game.Session.Combat, Game.Session.Self, Game.Session.Pet, Game.Requests.ClientSettings));
        Facet(
            () => { Chat = new ChatSystem(this, Game.Session.Chat, Game.Session.King, Game.Session.Zone, Game.Session.Clan, Game.Session.Self,
                Game.Session.Hostility, Game.Session.ZoneAbility, Game.Session.GmFx, Game.Requests.Chat, Game.Requests.King); Chat.Build(); },
            () => Chat?.Dispose());
        Features.Add(Log = new CombatLogFeature(this, Game.Session.Chat, Game.Session.Vanguard));
        Features.Add(MsgOut = new MsgOutFeature(this, SkillCastSystem));
        Log.MetaHoverStarted += meta => Shout?.OnLogMetaHover(meta);
        Facet(() => { Floaters = new FloaterSystem(this); Floaters.Build(); }, () => Floaters?.Dispose());
        Facet(CombatSystem.Start);
        Features.Add(BagCompanions = new BagCompanionFeature(this, Game.Session.Self));
        Features.Add(Cosmetics = new CosmeticsFeature(this, Game.Session.Cosmetics, Game.Session.Self, Game.Session.UserOption, Game.Requests.Cosmetics, Game.Requests.UserOption, StealthSystem, EntitySystem, EntityPlatesSystem));
        Features.Add(ItemTooltip = new ItemTooltipFeature(this, Game.Session.Pet, Game.Session.Self, Game.Session.ZoneAbility));
        Features.Add(Seal = new SealFeature(this, Game.Session.ItemUpgrade, Game.Session.Items, Game.Session.Self, Game.Requests.ItemUpgrade));
        Features.Add(PetHatch = new PetHatchFeature(this, Game.Session.Pet, Game.Session.Self, Game.Requests.Pet));
        Features.Add(Pet = new PetFeature(this, Game.Session.Pet, Game.Session.Self, Game.Requests.Pet, PetHatch));
        Features.Add(InventoryWindow = new InventoryFeature(this, Game.Session.Items, Game.Session.Pet, Game.Session.Progress, Game.Session.Self, Game.Session.Vendor, Game.Requests.Items, Cosmetics, ItemTooltip, Seal, Pet, ItemRouting));
        Features.Add(ItemRouting);
        Features.Add(MonsterStone = new MonsterStoneFeature(this, Game.Session.MonsterStone, Game.Session.Party, Game.Session.Self, Game.Requests.MonsterStone, BasicAttackSystem));
        Features.Add(Hotbar = new HotbarFeature(this, Game.Session.Chaos, Game.Session.Bifrost, Game.Session.Combat, Game.Session.Genie, Game.Session.Progress, Game.Session.Pvp, Game.Session.Self, Game.Session.SkillTimers, Game.Requests.Combat, MonsterStone, SoccerSystem, SkillCastSystem));
        Features.Add(ChaosSkillWindow = new ChaosSkillWindowFeature(this, Hotbar));
        Features.Add(PotionBar = new PotionBarFeature(this, Hotbar, Game.Session.Self, Game.Session.SkillTimers));
        Features.Add(SkillPreset = new SkillPresetFeature(this, Game.Session.Preset, Game.Requests.Preset, Game.Session.Self));
        Features.Add(SkillWindow = new SkillWindowFeature(this, Game.Session.Develop, Game.Requests.Develop, Hotbar, Game.Session.Self, SkillPreset));
        Features.Add(Stats = new StatsFeature(this, Game.Session.Clan, Game.Session.Develop, Game.Session.Items, Game.Session.Progress, Game.Session.Self, Game.Requests.Progress, Hotbar, SkillPreset, SkillWindow));
        Features.Add(Progression = new ProgressionFeature(this, Stats, EntityPlatesSystem, Game.Session.Self, SkillWindow, Game.Session.Progress, Game.Session.Loot));
        Features.Add(Friends = new FriendsFeature(this, Game.Session.Friends, Game.Session.Self, Game.Requests.Friends, PlayerMenu));
        Features.Add(ClanMarks);
        Features.Add(ClanCreate = new ClanCreateFeature(this, Game.Session.Clan, Game.Session.Self, Game.Requests.Clan));
        Features.Add(ClanHandover = new ClanHandoverFeature(this, Game.Session.Clan, Game.Session.Self, Game.Requests.Clan));
        Features.Add(ChatReactions = new ChatReactionsFeature(this, Game.Session.RoomMedia, Game.Requests.RoomMedia, PlayerMenu));
        Features.Add(Whispers = new WhispersFeature(this, Game.Session.Chat, Game.Session.RoomMedia, Game.Session.Self, Game.Session.ZoneAbility, Game.Requests.Chat, Game.Requests.RoomMedia, ChatReactions, PlayerMenu, Friends));
        Features.Add(NationChannels = new NationChannelsFeature(this, Game.Session.NationChat, Game.Requests.NationChat, () => ChatRoom.Show(), EntityPlatesSystem));
        Features.Add(ChatRoom = new ChatRoomFeature(this, Game.Session.Chat, Game.Session.NationChat, Game.Session.RoomMedia, Game.Session.Self, Game.Requests.NationChat, Game.Requests.RoomMedia,
            NationChannels, Whispers, ChatReactions, PlayerMenu));
        Features.Add(MailIcon = new MailIconFeature(this, Game.Session.Mail, TopIcons));
        Features.Add(Mail = new MailFeature(this, Game.Session.Mail, Game.Requests.Mail, MailIcon));
        Features.Add(MailCompose = new MailComposeFeature(this, Game.Session.Clan, Game.Session.Friends, Game.Session.Mail, Game.Session.Self, Game.Requests.Clan, Game.Requests.Friends, Game.Requests.Mail, Mail));
        Features.Add(ClanBoard = new ClanBoardFeature(this, Game.Session.Chat, Game.Session.Clan, Game.Session.ClanBoards, Game.Session.Self, Game.Requests.Chat, Game.Requests.Clan, Game.Requests.ClanBoards, ClanMarks, Whispers));
        Features.Add(WishBoard = new WishBoardFeature(this, Game.Session.Clan, Game.Session.ClanBoards, Game.Session.Self, Game.Requests.Chat, Game.Requests.ClanBoards, ClanBoard, MailCompose));
        Features.Add(Rank = new RankFeature(this, Game.Session.Rank, Game.Session.Self, Game.Requests.Rank));
        Features.Add(DailyRankWindow = new DailyRankFeature(this, Game.Session.DailyRank, Game.Requests.DailyRank, TopIcons));
        Features.Add(UserInfo = new UserInfoFeature(this, Game.Session.Clan, Game.Session.NearbyPlayers, Game.Session.PlayerInspect, Game.Requests.PlayerInspect, ClanMarks, DailyRankWindow, Game.Session.Friends));
        Features.Add(ClanPoints = new ClanPointsFeature(this, Game.Session.Clan, Game.Session.Progress, Game.Session.Self, Game.Requests.Clan));
        Features.Add(Clan = new ClanFeature(this, Game.Session.Clan, Game.Session.Self, Game.Requests.Clan, Whispers, ClanBoard, ClanCreate, ClanHandover, ClanMarks, UserInfo, PlayerMenu, ClanPoints));
        Facet(EntityPlatesSystem.ClanLooksInit, EntityPlatesSystem.ClanLooksDispose);
        Features.Add(ResetConfirm = new ResetConfirmFeature(this, Game.Session.Develop, Game.Requests.Develop));
        Features.Add(UserVoteWindow = new UserVoteFeature(this, Game.Session.NpcQuest, Game.Session.Party, Game.Requests.Party));
        Features.Add(PartyInvites = new PartyInvitesFeature(this, Game.Session.Party, Game.Session.UserOption, Game.Requests.Party));
        Features.Add(Party = new PartyFeature(this, Game.Session.Party, Game.Session.Self, Game.Requests.Party, UserVoteWindow, Game.Session.Entities, PlayerMenu, SkillCastSystem));
        Features.Add(PartyCommander = new PartyCommanderFeature(this, Game.Session.Clan, Game.Session.Party, Game.Session.Self, Game.Session.SkillTimers, Game.Requests.Party, Party));
        Features.Add(SeekParty = new SeekPartyFeature(this, Game.Session.Bifrost, Game.Session.Party, Game.Session.Self, Game.Requests.Bifrost, Game.Requests.Party, Game.Requests.Shout, EntryPopups, PartyInvites));
        Features.Add(WarBoard = new PartyWarBoardFeature(this, Game.Session.Party, Game.Session.PartyWarBoard, Game.Session.Self, Game.Requests.PartyWarBoard, Party));
        Features.Add(Loot = new LootFeature(this, Game.Session.Items, Game.Session.Loot, Game.Session.Self, Game.Requests.Items, LootFilterWindow, Pet, EntityActionSystem, EntitySystem));
        Features.Add(QuestNotifications = new QuestNotificationsFeature(this, Game.Session.Self, Game.Requests.NpcQuest));
        Features.Add(EventPlates = new EventPlatesFeature(this, TargetHud));
        Features.Add(QuestProgressToast = new QuestProgressToastFeature(this, EventPlates, Game.Session.QuestBook, Game.Session.Self));
        Features.Add(QuestMarkers = new QuestMarkersFeature(this, Game.Session.QuestBook, EntitySystem));
        Features.Add(QuestTracker = new QuestTrackerFeature(this, Game.Session.QuestBook, QuestProgressToast, Game.Session.Self));
        Features.Add(Quest = new QuestFeature(this, Game.Session.NpcQuest, Game.Session.QuestBook, Game.Session.Self, Game.Requests.NpcQuest, QuestMarkers, Map, QuestTracker));
        Features.Add(QuestTarget = new QuestTargetFeature(this, Game.Session.NpcQuest, Map, Game.Session.Self));
        Features.Add(QuestReceipt = new QuestReceiptFeature(this, Game.Session.NpcQuest, Game.Session.QuestBook));
        Features.Add(NpcDialogWindow = new NpcDialogFeature(this, Game.Session.NpcQuest, Game.Session.QuestBook, Game.Session.Self, Game.Requests.NpcQuest, ItemTooltip, QuestNotifications, NpcInteraction, NpcInteraction));
        Features.Add(NpcInteraction);
        Features.Add(MainWindows = new MainWindowsFeature(this, this, Game.Session.Self, BagCompanions));
        Features.Add(ChatHost = new ChatHostFeature(this, this, Game.Session.Clan, Game.Session.NearbyPlayers, Game.Session.Party, Game.Session.Self, Game.Session.Friends, Game.Requests.NearbyPlayers));
        Features.Add(HudLauncher = new HudLauncherFeature(this, StatusHud, () => TownRecall.Press(), () => SeekParty.ToggleSeek(), () => Genie.Toggle(),
            Game.Requests.ClientSettings));
        if (Platform.TouchUi)
        {
            Facet(TouchHudInit, TouchHudDispose);
            Features.Add(ExpBar = new ExpBarFeature(this, Game.Session.ExpSeal, Game.Session.Self, ScenerySystem));
        }
        Features.Add(Vendor = new VendorFeature(this, Game.Session.Progress, Game.Session.Self, Game.Session.NpcQuest, Game.Session.Vendor, Game.Session.ZoneAbility, Game.Requests.Vendor, NpcInteraction));
        Features.Add(Upgrade = new UpgradeFeature(this, Game.Session.ItemUpgrade, Game.Session.Items, Game.Session.Self, Game.Requests.ItemUpgrade, Vendor, NpcInteraction));
        Features.Add(PieceChange = new PieceChangeFeature(this, Game.Session.Items, Game.Session.PieceChange, Game.Session.Self, Game.Requests.PieceChange, Vendor, Upgrade, NpcInteraction));
        Features.Add(ItemSmashWindow = new ItemSmashFeature(this, Game.Session.ItemSmash, Game.Session.Self, Game.Requests.ItemSmash, Vendor));
        Features.Add(Repair = new RepairFeature(this, Game.Session.Items, Game.Session.NpcQuest, Game.Session.Progress, Game.Session.Repair, Game.Session.Self, Game.Requests.Repair, Vendor, NpcInteraction));
        Features.Add(Warehouse = new WarehouseFeature(this, Game.Session.NpcQuest, Game.Session.Progress, Game.Session.Self, Game.Session.Warehouse, Game.Requests.Warehouse, NpcInteraction));
        Features.Add(ClassChange = new ClassChangeFeature(this, Game.Session.Develop, Game.Session.NpcQuest, ResetConfirm, NpcInteraction));
        Features.Add(CharacterSealWindow = new CharacterSealFeature(this, Game.Session.CharacterSeal, Game.Session.Items, Game.Session.Pet, Game.Session.Self, Game.Requests.CharacterSeal));
        Features.Add(Exchange = new ExchangeFeature(this, Game.Session.Exchange, Game.Session.Genie, Game.Session.Progress, Game.Session.Self, Game.Session.Zone, Game.Session.ZoneAbility, Game.Requests.Exchange, Vendor, Warehouse, CharacterSealWindow, EntityStatesSystem));
        Facet(GatherSystem.GatherInit, GatherSystem.GatherDispose);
        Features.Add(MerchantStalls = new MerchantStallsFeature(this, Game.Session.Merchant, Game.Requests.Merchant, EntitySystem, EntityPlatesSystem));
        Features.Add(MarketPrice = new MarketPriceFeature(this, Game.Session.MarketPrice, Game.Requests.MarketPrice, Game.Session.Premium));
        Features.Add(Merchant = new MerchantFeature(this, Game.Session.Merchant, Game.Session.Premium, Game.Session.Progress, Game.Session.Self, Game.Requests.Merchant, EntityPlatesSystem, PlayerMenu, MerchantStalls, MarketPrice, Game.Session.SlaveMerchant, Game.Requests.SlaveMerchant));
        Features.Add(SlaveMerchantWindow = new SlaveMerchantFeature(this, Game.Session.Self, Game.Session.SlaveMerchant, Game.Requests.SlaveMerchant, Merchant));
        Features.Add(MerchantSearch = new MerchantSearchFeature(this, MarketPrice, Game.Requests.Merchant, Game.Requests.MerchantSearch, Game.Session.MerchantSearch, MerchantStalls, Whispers, Hotbar));
        Features.Add(MerchantBuy = new MerchantBuyFeature(this, Merchant, Game.Session.Merchant, Game.Requests.Merchant, Game.Session.Self, MerchantStalls, Game.Session.Progress));
        Features.Add(Premium = new PremiumFeature(this, Game.Session.Clan, Game.Session.Premium, Game.Session.Progress, Game.Requests.Premium, TopIcons));
        Features.Add(EventTitlesWindow = new EventTitlesFeature(this, Game.Session.Zone, Premium));
        Features.Add(SeasonalWindow = new SeasonalFeature(this, Game.Session.Seasonal));
        Features.Add(ZoneAbility = new ZoneAbilityFeature(this, Game.Session.Entities, Game.Session.ZoneAbility, Game.Session.Hostility, Game.Session.Siege));
        Features.Add(Shout = new ShoutFeature(this, Game.Session.Shout, Game.Requests.Shout));
        Features.Add(ChaosGuide = new ChaosGuideFeature(this, EntryPopups, Rank));
        Features.Add(Chaos = new ChaosFeature(this, Game.Session.Bifrost, Game.Session.Chaos, Game.Session.MonsterStone, Game.Session.Self, Game.Requests.Rank, MsgOut, Rank, EventPlates, ChaosGuide));
        Features.Add(KnightRoyaleWindow = new KnightRoyaleFeature(this, Game.Session.KnightRoyale, Game.Requests.KnightRoyale, Map));
        Features.Add(Challenge = new ChallengeFeature(this, Game.Session.Challenge, Game.Requests.Challenge));
        Features.Add(Warp = new WarpFeature(this, Game.Session.NpcQuest, Game.Session.Progress, Game.Session.Self, Game.Session.Warp, Game.Requests.Warp, MapInteractablesSystem, NpcInteraction));
        Features.Add(PowerUpStoreGift = new PowerUpStoreGiftFeature(Game.Session.Clan, Game.Session.Friends, Game.Session.Self, Game.Session.ShoppingMall, Game.Requests.Clan, Game.Requests.Friends, Game.Requests.ShoppingMall));
        Features.Add(ShoppingMall = new ShoppingMallFeature(this, Game.Session.Self, Game.Session.ShoppingMall, Game.Requests.ShoppingMall, PowerUpStoreGift));
        Features.Add(BattleEvent = new BattleEventFeature(this, Game.Session.BattleEvent, Game.Session.Self, Game.Requests.BattleEvent, EventPlates));
        Features.Add(PetSkillBar = new PetSkillBarFeature(this, Hotbar, Game.Session.Combat, Game.Session.Pet, Game.Requests.Pet));
        Features.Add(Rebirth = new RebirthFeature(this, Game.Session.Develop, Game.Session.Self, Game.Requests.Develop));
        Features.Add(GenderChangeWindow = new GenderChangeFeature(this, Game.Session.GenderChange, Game.Session.Self, Game.Requests.GenderChange));
        Features.Add(NationTransfer = new NationTransferFeature(this, Game.Session.NationTransfer, Game.Requests.NationTransfer));
        Features.Add(Bifrost = new BifrostFeature(this, Game.Session.Bifrost, Game.Session.Self, Game.Requests.Bifrost, EventPlates, MsgOut));
        Features.Add(BorderDefenseWar = new BorderDefenseWarFeature(this, Game.Session.Bifrost, Game.Session.Rank, Game.Session.Self, Game.Requests.Rank, EventPlates));
        Features.Add(NameChange = new NameChangeFeature(this, Game.Session.NameChange, Game.Session.Self, Game.Requests.Clan, Game.Requests.NameChange));
        Features.Add(KingElection = new KingElectionFeature(this, Game.Session.King, Game.Session.NpcQuest, Game.Requests.King, NpcInteraction));
        Features.Add(KingTreasury = new KingTreasuryFeature(this, Game.Session.King, Game.Session.NpcQuest, Game.Session.Self, Game.Requests.King, NpcInteraction));
        Features.Add(Siege = new SiegeFeature(this, Game.Session.Clan, Game.Session.NpcQuest, Game.Session.Siege, Game.Session.ZoneAbility, Game.Requests.Siege, TopIcons, NpcInteraction));
        Features.Add(Pvp = new PvpFeature(this, Game.Session.Pvp, Game.Session.Self));
        Facet(EntityStatesSystem.StateVisualInit, EntityStatesSystem.StateVisualDispose);
        Facet(EntityStatesSystem.RespawnBlinkInit, EntityStatesSystem.RespawnBlinkDispose);
        Facet(EntityStatesSystem.SizeStateInit, EntityStatesSystem.SizeStateDispose);
        Facet(BuffSystem.GroundEffectInit, BuffSystem.GroundEffectDispose);
        Features.Add(AngerGaugeWindow = new AngerGaugeFeature(this, Game.Session.Pvp, Game.Session.Self));
        Features.Add(DeathDialog = new DeathDialogFeature(this, Game.Session.Progress, Game.Session.Self, Game.Requests.Combat, KnightRoyaleWindow, CombatSystem));
        Features.Add(ResurrectOffer = new ResurrectOfferFeature(this, Game.Session.Combat, Game.Session.Self, Game.Requests.Combat, DeathDialog, CombatSystem));
        Features.Add(HitFlash = new HitFlashFeature(this, EntitySystem));
        Features.Add(RenderFault = new RenderFaultFeature(this));
        Features.Add(Portrait = new PortraitFeature(this));
        Features.Add(NpcGates = new NpcGatesFeature(this));
        Features.Add(RankerStatueWindow = new RankerStatueFeature(this, EntitySystem));
        Features.Add(ControlState = new ControlStateFeature(this, Game.Session.TargetState));
        Features.Add(ClanWarehouse = new ClanWarehouseFeature(this, Game.Session.Clan, Game.Session.ClanWarehouse, Game.Session.Progress, Game.Session.Self, Game.Requests.ClanWarehouse, Warehouse, NpcInteraction));
        Features.Add(VipWarehouse = new VipWarehouseFeature(this, Game.Session.Self, Game.Session.VipWarehouse, Game.Requests.VipWarehouse, Warehouse, NpcInteraction));
        Features.Add(UiOpacity = new UiOpacityFeature(this));
        Facet(() => NamePlate.RescaledEvent += EntityPlatesSystem.OnNamePlatesRescaled, () => NamePlate.RescaledEvent -= EntityPlatesSystem.OnNamePlatesRescaled);
        Features.Add(ClanBattle = new ClanBattleFeature(this, Game.Session.ClanBattle, EventPlates));
        Features.Add(ClanPremium = new ClanPremiumFeature(this, Game.Session.Clan, Game.Session.ClanPremium, Game.Session.Premium, Game.Requests.ClanPremium));
        Facet(GateSystem.ObjectEventInit, GateSystem.ObjectEventDispose);
        Features.Add(Report = new ReportFeature(this, Game.Session.Report, Game.Requests.Report));
        Facet(StealthSystem.StealthInit, StealthSystem.StealthDispose);
        Features.Add(Awaken = new AwakenFeature(this, Game.Session.Awaken));
        Features.Add(ChangeHair = new ChangeHairFeature(this, Game.Session.ChangeHair, Game.Session.Self, Game.Requests.ChangeHair, GenderChangeWindow));
        Features.Add(TownRecall = new TownRecallFeature(this, Game.Session.Self, Game.Requests.Player, SiegeTowerSystem));
        Features.Add(SpecialAuctionWindow = new SpecialAuctionFeature(this, Game.Session.Self, Game.Session.SpecialAuction, Game.Requests.SpecialAuction, UpgradeNotice));
        Features.Add(Attendance = new AttendanceFeature(this, Game.Session.Attendance, Game.Requests.Attendance, TopIcons));
        Features.Add(AchievementTrophy = new AchievementTrophyFeature(this, Game.Session.Achievement, TopIcons));
        Features.Add(TitlePicker = new TitlePickerFeature(this, Game.Session.Achievement, Game.Requests.Achievement));
        Features.Add(Achievement = new AchievementFeature(this, Game.Session.Achievement, Game.Requests.Achievement, AchievementTrophy, TitlePicker));
        Features.Add(Lottery = new LotteryFeature(this, Game.Session.Lottery, Game.Session.Self, Game.Requests.Lottery, TopIcons));
        Features.Add(Events = new EventsFeature(this, Game.Session.EventSchedule, Game.Session.Self, Game.Requests.EventSchedule, Bifrost, Lottery, TopIcons));
        Facet(EntityPlatesSystem.SiegeNamesInit, EntityPlatesSystem.SiegeNamesDispose);
        Facet(SiegeTowerSystem.SiegeTowerInit, SiegeTowerSystem.SiegeTowerDispose);
        Features.Add(VanguardWindow = new VanguardFeature(this, Game.Session.Self, Game.Session.Vanguard, Game.Requests.Vanguard, Merchant, Map));
        Features.Add(Nereid = new NereidFeature(this, Game.Session.BattleEvent, Game.Session.Self, BattleEvent, Map));
        Features.Add(ExpSealWindow = new ExpSealFeature(this, Game.Session.ExpSeal, Game.Session.Self, Game.Requests.ExpSeal, ExpBar));
        Features.Add(CommandList = new CommandListFeature(this, this, Game.Session.Clan, Game.Session.Party, Game.Session.UserOption, Game.Requests.Chat, Game.Requests.Clan, Game.Requests.ClanBattleWar, Game.Requests.UserOption));
        Features.Add(ClanMatchWindow = new ClanMatchFeature(this, Game.Session.BattleEvent, Game.Session.Clan, EntitySystem, EntityPlatesSystem));
        Features.Add(Bounty = new BountyFeature(this, Game.Session.Bounty, Game.Requests.Bounty, Game.Session.Self));
        Features.Add(Tournament = new TournamentFeature(this, Game.Session.Tournament, Game.Requests.Tournament));
        Features.Add(DisguiseWindow = new DisguiseFeature(this, Game.Session.Disguise, Game.Session.Premium, Game.Session.Self, Game.Requests.Combat));
        Features.Add(Messenger = new MessengerFeature(this, Game.Session.Messenger, Game.Requests.Messenger));
        Features.Add(Forces = new ForcesFeature(this, Game.Session.Forces, Game.Requests.Forces));
        Features.Add(KillSplash = new KillSplashFeature(this, Game.Session.KillAssist));
        Features.Add(Krowaz = new KrowazFeature(this, Game.Session.Chat));
        Features.Add(ChatNotices = new ChatNoticesFeature(this, Game.Session.Chat, Game.Session.Self));
        Features.Add(Fortune = new FortuneFeature(this));
        Features.Add(Yut = new YutGameFeature(this, Game.Session.Mining, Game.Session.Self, Game.Requests.Mining));
        Features.Add(DrakiBoard = new DrakiBoardFeature(this, Game.Session.Bifrost, Game.Session.Self, Game.Requests.Bifrost));
        Features.Add(ItemCombineWindow = new ItemCombineFeature(this, Game.Session.ItemCombine, Game.Session.Self, Game.Requests.ItemCombine, Vendor, NpcInteraction));
        Features.Add(Roulette = new RouletteFeature(this, Game.Session.Roulette, Game.Requests.Roulette));
        Features.Add(FishingHall = new FishingHallFeature(this, Game.Session.FishingHall, Game.Session.Self, Game.Requests.FishingHall));
        Features.Add(PluginBridge);
        Facet(ConnectPlugins, PluginBridge.Disconnect);
        Features.Add(GuardPet = new GuardPetFeature(this, Game.Session.GuardPet, Game.Requests.GuardPet));
        Features.Add(GlobalMap = new GlobalMapFeature(this, Game.Session.GlobalMap, Game.Requests.GlobalMap));
        Features.Add(GenieHammer = new GenieHammerFeature(this, Game.Requests.Genie, Game.Session.Self, Game.Session.Genie));
        Features.Add(GeniePotions = new GeniePotionsFeature(this, Hotbar, Game.Session.Pet, Game.Requests.Pet, Pet, Game.Session.Self, PotionBar, PetSkillBar));
        Features.Add(GenieScrolls = new GenieScrollsFeature(this, Game.Session.Genie, Hotbar, Game.Session.Self, SkillCastSystem));
        Features.Add(GenieTown = new GenieTownFeature(this, GeniePotions, Game.Session.Party, Game.Requests.Genie, Game.Session.Self, TownRecall));
        Features.Add(Genie = new GenieFeature(this, Game.Session.Chat, Game.Session.Combat, Game.Session.Genie, Game.Session.Party, Game.Session.Self, Game.Requests.Genie, PartyInvites, EntityActionSystem, MerchantStalls, SkillCastSystem, BasicAttackSystem, GenieHammer, GeniePotions, GenieScrolls, GenieTown));
        Features.Add(GenieSub = new GenieSubFeature(this, Genie, Game.Session.Genie));
        Facet(EntityStatesSystem.GenieAuraInit, EntityStatesSystem.GenieAuraDispose);
        Facet(() => PluginHost.Game.AttachGenie(CreatePluginGenie()));
        Features.Add(CollectionRace = new CollectionRaceFeature(this, Game.Session.CollectionRace, Game.Requests.CollectionRace));
        Features.Add(Rental = new RentalFeature(this, Game.Session.Rental));
        Features.Add(PlayerMenu);
        Features.Add(EquipView = new EquipViewFeature(this, Game.Session.PlayerInspect, Game.Requests.PlayerInspect));
        Features.Add(Cursor = new CursorFeature(this, Game.Session.Self, BuffSystem, Repair, Selection, BasicAttackSystem));
        Features.Add(AdminPanel = new AdminPanelFeature(this, Game.Session.Admin, Game.Session.Self, Game.Session.ShoppingMall, Game.Requests.Admin, Stats, PlayerSystem, SkillWindow));
        Features.Add(GmFx = new GmFxFeature(this, Game.Session.GmFx, EntityLooksSystem, EntitySystem));
        Features.Add(RankerMessageWindow = new RankerMessageFeature(this, Game.Session.RankerMessage, Game.Session.Self, Game.Requests.RankerMessage));
        Features.Add(RankBoardWindow = new RankBoardFeature(this, Game.Session.BattleEvent, Game.Session.Self, Rank, ClanMarks));
        Features.Add(HpStatus = new HpStatusFeature(this, Game.Session.Party, Game.Session.ZoneAbility, StatusHud, TargetHud, PluginBridge));
        Features.Add(ClanBattleWar = new ClanBattleWarFeature(this, Game.Session.Clan, Game.Session.ClanBattleWar, Game.Session.ZoneAbility, Game.Requests.ClanBattleWar, ZoneAbility));
        Features.Add(TimedItemsWindow = new TimedItemsFeature(this, Game.Session.Items, Game.Session.Pet, Game.Session.Self, EntryPopups, TopIcons));
        Features.Add(Repurchase = new RepurchaseFeature(this, Game.Session.Self, Game.Session.Vendor, Game.Requests.Vendor, NpcInteraction));
        Facet(() =>
        {
            SoccerSystem.AttachPlates(EventPlates);
            SoccerSystem.SoccerInit();
        }, SoccerSystem.SoccerDispose);
        Facet(BuffSystem.ContinuanceInit, BuffSystem.ContinuanceDispose);
        Facet(Pet.PlateInit);
        Facet(FxLayerInit, FxLayerDispose);
        Facet(CentredWindowsInit);
        Features.Add(AudioSystem);
        Features.Add(PlayerInput = new PlayerInputFeature(this, Avatar));
        Features.Add(Hotkeys = new HotkeysFeature(this, this));
        Features.BuildPending();
    }

    private FxLayer? fxLayer;

    private void FxLayerInit() => fxLayer = FxLayer.Attach(GetViewport());

    private void FxLayerDispose()
    {
        if (fxLayer != null && GodotObject.IsInstanceValid(fxLayer)) fxLayer.QueueFree();
        fxLayer = null;
    }

    internal void TeardownFacets() => Features.Dispose();

    internal void ConnectPlugins() => PluginBridge.Connect(CreatePluginAdapters());

    internal PluginGenieBridge CreatePluginGenie() => new(Genie, GeniePotions, GenieSub, PluginBridge, Log, Game.Session.Genie, Game.Session.Self, Game.Requests.Genie);

    internal PluginAdapters CreatePluginAdapters()
    {
        var target = new PluginTargetBridge(Selection, EntitySystem, EntityPlatesSystem, LootFilterWindow, TargetQuestDrops, Game.Session.Party);
        return new PluginAdapters(
            new PluginCharacterBridge(InventoryWindow, EntityPlatesSystem, Stats, Cosmetics, Portrait, Game.Session.Self, Game.Session.Progress, Game.Session.Items,
                Game.Session.UserOption, Game.Session.Mail, Game.Session.Clan, Game.Requests.UserOption),
            new PluginInventoryBridge(InventoryWindow, ItemRouting, ItemTooltip, Game.Session.Self, Game.Requests.Items),
            target,
            new PluginWindowsBridge(TopIcons, MainWindows, SkillPreset, TitlePicker),
            new PluginChatBridge(() => Chat),
            new PluginHotbarBridge(Hotbar, ItemTooltip, Game.Session.SkillTimers, Game.Session.Self),
            new PluginMapBridge(this, VanguardWindow, Avatar, Map, NpcFinderWindow, ScenerySystem, Game.Session.BattleEvent),
            new PluginCommandsBridge(TownRecall, PlayerSystem, Log, Exchange, PlayerMenu, GameMenu, Hotkeys, target),
            new PluginLogBridge(PluginBridge, Game.Session.Zone),
            new PluginQuestsBridge(QuestTracker),
            new PluginSkillsBridge(SkillCastSystem, SkillWindow, Hotbar, Game.Session.Self),
            new PluginServiceBridge(Vendor, ItemTooltip, Warehouse, VipWarehouse, Game.Session.NpcQuest),
            new PluginSocialBridge(Warp, Whispers, PluginBridge, () => Chat, PlayerMenu, Party, PartyCommander, WarBoard, this, UserVoteWindow, Exchange, ItemTooltip,
                Game.Session.RoomMedia, Game.Session.Chat, Game.Session.Party, Game.Session.Self),
            new PluginAppearanceBridge(false, GenderChangeWindow, ChangeHair, this, Game.Session.Self),
            new PluginAppearanceBridge(true, GenderChangeWindow, ChangeHair, this, Game.Session.Self),
            new PluginFriendsBridge(Friends, Game.Session.Friends, Whispers, PlayerMenu, this, Game.Session.Self, Game.Requests.Friends));
    }

    private const string CentreOnOpenMigration = "centre_on_open";

    private void CentredWindowsInit() =>
        Config.ForgetWindowPositionsOnce(CentreOnOpenMigration, HudWindow.CentredIds);

    // Order is precedence, not style: the first open entry wins. Insert deliberately, not at the end.
    private IEscapable[] escapeOrder = [];

    private void BuildEscapeOrder() => escapeOrder =
    [
        SkillAreaSystem, Map, QuestNotifications, NpcDialogWindow, Vendor, Loot, Repair, Warehouse.AmountPromptPanel, Events,
        Warehouse, Upgrade, Seal, PieceChange, ClassChange, GenderChangeWindow, NationTransfer, NameChange, Exchange,
        MarketPrice, Merchant, MerchantSearch, MerchantBuy, SlaveMerchantWindow, Merchant.SellStallAndMenu, ClanCreate, ClanPoints, Warp, Rank, PetHatch, Pet, ShoppingMall, Rebirth, AdminPanel.SpawnPanel, KingTreasury, KingElection, Siege,
        CapeWindow, ClanWarehouse.AmountPromptPanel, ClanWarehouse, VipWarehouse.AmountPromptPanel, VipWarehouse, Report, EquipView, ChangeHair, Achievement, MailCompose, Mail, Lottery,
        SpecialAuctionWindow, Attendance, Bounty, Tournament, DisguiseWindow, SkillPreset, TitlePicker, ChatHost,
        Messenger, Forces, ChatRoom, NationChannels, Fortune, DrakiBoard, ItemCombineWindow, Roulette, FishingHall, GlobalMap, Genie, AdminPanel,
        new EscapeGroup(() => PluginHost.Ui.EscapePanels), MainWindows, Whispers.Panel,
    ];

    internal void HandleEscape()
    {
        if (DeathDialog.Layer is { Visible: true } || !EscapeOrder.CloseFirst(escapeOrder)) GameMenu.Toggle();
    }

    private readonly Dictionary<LibreKO.Plugins.HudPart, Control> pluginHud = new();

    private void PluginHudSeam(CanvasLayer nativeLayer, LibreKO.Plugins.HudPart part)
    {
        if (!PluginHost.Ui.HudHidden(part)) return;
        nativeLayer.Visible = false;
        if (PluginHost.Ui.HudReplacement(part) is not { } build) return;
        var layer = new CanvasLayer { Layer = nativeLayer.Layer };
        AddChild(layer);
        var control = build();
        layer.AddChild(control);
        pluginHud[part] = control;
    }

    private void ApplyHudTheme()
    {
        var theme = HudTheme.Shared;
        foreach (var child in GetChildren())
        {
            if (child is not CanvasLayer layer) continue;
            foreach (var node in layer.GetChildren())
                if (node is Control ctrl && ctrl.Theme == null)
                    ctrl.Theme = theme;
        }
    }

    private List<Rect2> DockHudRects()
    {
        var rects = new List<Rect2>();
        foreach (var hud in new Control?[]
                 {
                     Map.MiniMap, ScenerySystem.ClockLabel, HudLauncher.Root, HudLauncher.TownButton, AchievementTrophy.Trophy, Attendance.Gift,
                     MailIcon.IconButton, TopIcons.Icon(TopIconSlot.PowerUpStore), TopIcons.Icon(TopIconSlot.Lottery), StatusHud.Root, Hotbar.Box, BuffSystem.BuffPanel,
                     Premium.Chip, Log.Frame, Chat?.Panel,
                 })
            if (hud != null && GodotObject.IsInstanceValid(hud) && hud.IsVisibleInTree())
                rects.Add(hud.GetGlobalRect());
        return rects;
    }

    private void TouchHudInit()
    {
        Hotbar.ScaleVitals(StatusHud.Root);
        TouchControlsSystem.TouchControlsInit();
        PotionBar.BuildPotionBar();
    }

    private void TouchHudDispose()
    {
        TouchControlsSystem.TouchControlsDispose();
        PotionBar.PotionBarDispose();
    }
}
