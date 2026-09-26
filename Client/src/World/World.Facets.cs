using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

// Order is load-bearing: later facets read what earlier ones built. Teardown runs in the same order.
public partial class World : Node3D
{
    private readonly List<Action> _facetTeardown = new();

    private void Facet(Action init, Action? teardown = null)
    {
        init();
        if (teardown != null) _facetTeardown.Add(teardown);
    }

    private void BuildFacets()
    {
        Facet(BuildStatusHud);
        Facet(BuildBuffBar);
        Facet(BuildMiniMap);
        Facet(BuildFullMap);
        Facet(BuildClockHud);
        Facet(BuildSelectionRing);
        Facet(BuildObjectHighlight);
        Facet(BuildTargetHud);
        if (Config.Development)
            Facet(BuildInfoPanel);
        Facet(BuildEscMenu);
        Facet(
            () => { Chat = new ChatSystem(this); Chat.Build(); Chat.LocalCommand = RunLocalCommand; },
            () => Chat?.Dispose());
        Facet(BuildCombatLog, CombatLogDispose);
        Facet(() => { Floaters = new FloaterSystem(this); Floaters.Build(); }, () => Floaters?.Dispose());
        Facet(CombatInit);
        Facet(InventoryInit);
        Facet(StatsInit, StatsDispose);
        Facet(FriendsInit, FriendsDispose);
        Facet(ResetConfirmInit, ResetConfirmDispose);
        Facet(HotbarInit, HotbarDispose);
        Facet(SkillWindowInit, SkillWindowDispose);
        Facet(PartyInit, PartyDispose);
        Facet(LootInit, LootDispose);
        Facet(NpcInit, NpcDispose);
        Facet(QuestInit, QuestDispose);
        Facet(MainPanelInit);
        Facet(() => BuildHudLauncher());
        if (Platform.TouchUi) Facet(TouchHudInit, TouchHudDispose);
        Facet(VendorInit, VendorDispose);
        Facet(UpgradeInit, UpgradeDispose);
        Facet(PieceChangeInit, PieceChangeDispose);
        Facet(RepairInit, RepairDispose);
        Facet(WarehouseInit, WarehouseDispose);
        Facet(ClassChangeInit, ClassChangeDispose);
        Facet(ExchangeInit, ExchangeDispose);
        Facet(GatherInit, GatherDispose);
        Facet(MerchantInit, MerchantDispose);
        Facet(ClanInit, ClanDispose);
        Facet(CosmeticsInit, CosmeticsDispose);
        Facet(PremiumInit, PremiumDispose);
        Facet(SeasonalInit, SeasonalDispose);
        Facet(ZoneAbilityInit, ZoneAbilityDispose);
        Facet(ShoutInit, ShoutDispose);
        Facet(RankInit, RankDispose);
        Facet(ChallengeInit, ChallengeDispose);
        Facet(WarpInit, WarpDispose);
        Facet(ShoppingMallInit, ShoppingMallDispose);
        Facet(BattleEventInit, BattleEventDispose);
        Facet(PetInit, PetDispose);
        Facet(RebirthInit, RebirthDispose);
        Facet(BifrostInit, BifrostDispose);
        Facet(CapeInit, CapeDispose);
        Facet(NameChangeInit, NameChangeDispose);
        Facet(KingInit, KingDispose);
        Facet(SiegeInit, SiegeDispose);
        Facet(PvpInit, PvpDispose);
        Facet(StateVisualInit, StateVisualDispose);
        Facet(DeathDialogInit, DeathDialogDispose);
        Facet(SealInit, SealDispose);
        Facet(ClanWarehouseInit, ClanWarehouseDispose);
        Facet(VipWarehouseInit, VipWarehouseDispose);
        Facet(ClanBattleInit, ClanBattleDispose);
        Facet(ClanPremiumInit, ClanPremiumDispose);
        Facet(ObjectEventInit, ObjectEventDispose);
        Facet(ReportInit, ReportDispose);
        Facet(StealthInit, StealthDispose);
        Facet(AwakenInit, AwakenDispose);
        Facet(ChangeHairInit, ChangeHairDispose);
        Facet(TownRecallInit);
        Facet(UpgradeNoticeInit, UpgradeNoticeDispose);
        Facet(MailInit, MailDispose);
        Facet(AuctionInit, AuctionDispose);
        Facet(AttendanceInit, AttendanceDispose);
        Facet(AchievementInit, AchievementDispose);
        Facet(MailIconInit, MailIconDispose);
        Facet(BountyInit, BountyDispose);
        Facet(TournamentInit, TournamentDispose);
        Facet(DisguiseInit, DisguiseDispose);
        Facet(PresetInit, PresetDispose);
        Facet(MessengerInit, MessengerDispose);
        Facet(ForcesInit, ForcesDispose);
        Facet(InstanceInit, InstanceDispose);
        Facet(ChatRoomInit, ChatRoomDispose);
        Facet(FortuneInit, FortuneDispose);
        Facet(ItemCombineInit, ItemCombineDispose);
        Facet(RouletteInit, RouletteDispose);
        Facet(FishingHallInit, FishingHallDispose);
        Facet(MarketBbsInit, MarketBbsDispose);
        Facet(DuelInit, DuelDispose);
        Facet(ItemExchangeInit, ItemExchangeDispose);
        Facet(RingUpgradeInit, RingUpgradeDispose);
        Facet(PluginBridgeInit, PluginBridgeDispose);
        Facet(InnInit, InnDispose);
        Facet(GuardPetInit, GuardPetDispose);
        Facet(EventQuestInit, EventQuestDispose);
        Facet(GlobalMapInit, GlobalMapDispose);
        Facet(GenieInit, GenieDispose);
        Facet(DailyQuestInit, DailyQuestDispose);
        Facet(CollectionRaceInit, CollectionRaceDispose);
        Facet(LotteryInit, LotteryDispose);
        Facet(RentalInit, RentalDispose);
        Facet(PlayerMenuInit, PlayerMenuDispose);
        Facet(UserInfoInit, UserInfoDispose);
        Facet(EquipViewInit, EquipViewDispose);
        Facet(WhisperInit, WhisperDispose);
        Facet(CursorInit, CursorDispose);
        Facet(AdminPanelInit, AdminPanelDispose);
        Facet(GmFxInit, GmFxDispose);
        Facet(BuildAudio, TeardownAudio);
    }

    private void TeardownFacets()
    {
        foreach (var teardown in _facetTeardown) teardown();
        _facetTeardown.Clear();
    }
}
